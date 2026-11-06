using System.Net;
using System.Text.Json.Serialization;
using GigaTodo;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<Database>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<RuntimeOptions>().BindConfiguration("Runtime").Validate(o => o.WallTimeSeconds >= 5 && o.MaxTurns > 0 && o.MaxToolCalls > 0 && o.Runner is "Disabled" or "Mock" or "Gigacode", "Некорректные Runtime настройки").Validate(RuntimeOptions.ValidConcurrency, "Runtime:MaxConcurrentAgents должен быть от 1 до 100").ValidateOnStart();
builder.Services.AddSingleton<RuntimeControl>();
builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<Workspace>();
builder.Services.AddOptions<AttachmentOptions>().BindConfiguration("Attachments").Validate(AttachmentOptions.Valid, "Некорректные ограничения вложений").ValidateOnStart();
builder.Services.AddSingleton<AttachmentStore>();
builder.Services.AddSingleton(AgentCatalog.Default);
builder.Services.AddSingleton<MockRunner>();
builder.Services.AddSingleton<GigacodeRunner>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<Dispatcher>();
builder.Services.AddScoped<Scheduler>();
builder.Services.AddHostedService<RuntimeWorker>();
var app = builder.Build();
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RuntimeOptions>>().Value;

app.Use(async (context, next) =>
{
    try
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed) || parsed.Authority != context.Request.Host.Value || parsed.Scheme != context.Request.Scheme))
            throw new RuleException("Недопустимый Origin", 403);
        if (context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote)) throw new RuleException("Доступ только с локального компьютера", 403);
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'";
        await next(context);
    }
    catch (RuleException exception) { context.Response.StatusCode = exception.Status; await context.Response.WriteAsJsonAsync(new { error = exception.Message }); }
    catch (DbUpdateConcurrencyException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "Данные изменились. Обновите страницу." }); }
    catch (ArgumentException exception) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = exception.Message }); }
    catch (BadHttpRequestException exception) { context.Response.StatusCode = exception.StatusCode; await context.Response.WriteAsJsonAsync(new { error = "Некорректный запрос или превышен лимит загрузки" }); }
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (Database db, CancellationToken ct) => Results.Ok(new { database = await db.Database.CanConnectAsync(ct) }));
app.MapGet("/api/agent-catalog", (AgentCatalog catalog) => new { catalog = catalog.Document, hash = catalog.Hash });
app.MapGet("/api/agents", (AgentCatalog catalog) => catalog.Document.Profiles.Select(p => catalog.Configuration(p.BoardId, Enum.Parse<AgentRole>(p.Role))));
app.MapGet("/api/boards", async (Database db, CancellationToken ct) => await db.Boards.AsNoTracking().OrderBy(b => b.Id == "analytics" ? 0 : b.Id == "frontend" ? 1 : b.Id == "backend" ? 2 : 3).ToArrayAsync(ct));
app.MapPut("/api/boards/{id}", async (string id, BoardSettings r, Database db, TaskService service, CancellationToken ct) =>
{
    if (r.ExecutorLimit is < 1 or > 100 || r.CriticLimit is < 1 or > 100 || r.ReworkLimit is < 1 or > 20) throw new RuleException("Лимиты: агенты 1–100, возвраты 1–20");
    if (r.ApprovalMode is not ("default" or "auto-edit" or "auto" or "plan")) throw new RuleException("Неизвестный режим подтверждений");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var board = await service.LockBoard(id, ct);
    if (board.Version != r.Version) throw new RuleException("Настройки изменились", 409);
    board.ExecutorLimit = r.ExecutorLimit; board.CriticLimit = r.CriticLimit; board.ReworkLimit = r.ReworkLimit;
    board.DefaultRepositoriesJson = Data.Pack(r.DefaultRepositories); board.ExecutorInstructions = r.ExecutorInstructions;
    board.CriticInstructions = r.CriticInstructions; board.Model = r.Model; board.ApprovalMode = r.ApprovalMode; board.Version++;
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(board);
});
app.MapGet("/api/tasks", async (string boardId, Database db, CancellationToken ct) =>
    await db.Tasks.AsNoTracking().Where(t => t.BoardId == boardId).OrderBy(t => t.Position).ThenBy(t => t.CreatedAt).ToArrayAsync(ct));
app.MapPost("/api/tasks", async (CreateTaskRequest r, TaskService service, CancellationToken ct) =>
{
    var result = await service.Create(r, ct: ct); return Results.Ok(new { task = result.Task, created = result.Created });
});
app.MapGet("/api/tasks/{id:guid}", async (Guid id, TaskService service, Database db, AttachmentStore store, CancellationToken ct) =>
{
    var task = await service.Find(id, ct);
    var events = await db.Events.AsNoTracking().Where(e => e.TaskId == id).OrderByDescending(e => e.Id).Take(300).OrderBy(e => e.Id).ToArrayAsync(ct);
    var runs = await db.Runs.AsNoTracking().Where(r => r.TaskId == id).OrderByDescending(r => r.StartedAt).Select(r => new { r.Id, r.Role, r.State, r.StartedAt, r.FinishedAt, r.SessionId, r.Error, r.Runner, r.ProfileJson }).ToArrayAsync(ct);
    var children = await db.Tasks.AsNoTracking().Where(t => t.ParentId == id).Select(t => new { t.Id, t.Title, t.BoardId, t.Stage }).ToArrayAsync(ct);
    return Results.Ok(new { task, events, runs, children, attachments = await store.Describe(db, task.AttachmentIds, ct) });
});
app.MapPost("/api/tasks/{id:guid}/attachments", async (Guid id, AttachmentRequest r, TaskService service, CancellationToken ct) => { await service.SetAttachments(id, r, ct); return Results.NoContent(); });
app.MapPost("/api/attachments", async (HttpContext http, Database db, AttachmentStore store, CancellationToken ct) =>
{
    if (!http.Request.HasFormContentType) throw new RuleException("Нужна multipart/form-data загрузка");
    var feature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = store.Limits.UploadBytes + 1024 * 1024;
    IFormCollection form;
    try { form = await http.Request.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions { MultipartBodyLengthLimit = store.Limits.UploadBytes, ValueLengthLimit = 1024 }, ct); }
    catch (InvalidDataException) { throw new RuleException("Некорректная загрузка или превышен лимит размера", 413); }
    if (form.Files.Count is < 1 or > 20) throw new RuleException("Выберите от 1 до 20 файлов за загрузку");
    if (form.Files.Sum(f => f.Length) > store.Limits.UploadBytes) throw new RuleException($"Общий размер загрузки превышает {store.Limits.MaxUploadMegabytes} МБ", 413);
    var uploaded = new List<Attachment>();
    foreach (var file in form.Files) uploaded.Add(await store.Save(file, ct));
    db.Attachments.AddRange(uploaded); await db.SaveChangesAsync(ct);
    return Results.Ok(new { attachments = uploaded.Select(store.Describe) });
});
app.MapGet("/api/attachments", async (string ids, Database db, AttachmentStore store, CancellationToken ct) =>
{
    var parts = ids.Split(',', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length > 50 || parts.Any(p => !Guid.TryParse(p, out _))) throw new RuleException("Неверные ID вложений");
    return Results.Ok(await store.Describe(db, parts.Select(Guid.Parse).ToArray(), ct));
});
app.MapGet("/api/attachments/{id:guid}/files", async (Guid id, Database db, AttachmentStore store, CancellationToken ct) =>
    Results.Ok(store.Files(await db.Attachments.FindAsync([id], ct) ?? throw new RuleException("Вложение не найдено", 404))));
app.MapGet("/api/attachments/{id:guid}/download", async (Guid id, string? path, Database db, AttachmentStore store, CancellationToken ct) =>
{
    var a = await db.Attachments.FindAsync([id], ct) ?? throw new RuleException("Вложение не найдено", 404);
    return Results.File(store.Download(a, path), "application/octet-stream", path is null ? a.FileName : Path.GetFileName(path));
});
app.MapPost("/api/tasks/reorder", async (ReorderRequest r, TaskService s, CancellationToken ct) => { await s.Reorder(r, ct); return Results.NoContent(); });
app.MapPost("/api/tasks/{id:guid}/review", async (Guid id, ReviewRequest r, TaskService s, CancellationToken ct) => { await s.HumanReview(id, r, ct); return Results.NoContent(); });
app.MapPost("/api/tasks/{id:guid}/pause", async (Guid id, VersionRequest r, TaskService s, CancellationToken ct) => { await s.Pause(id, r.Version, true, ct); return Results.NoContent(); });
app.MapPost("/api/tasks/{id:guid}/resume", async (Guid id, VersionRequest r, TaskService s, CancellationToken ct) => { await s.Pause(id, r.Version, false, ct); return Results.NoContent(); });
app.MapPost("/api/tasks/{id:guid}/messages", async (Guid id, MessageRequest r, TaskService s, CancellationToken ct) => { await s.Message(id, r, ct); return Results.NoContent(); });
app.MapPost("/api/tasks/{id:guid}/reset-sessions", async (Guid id, VersionRequest r, TaskService s, CancellationToken ct) => { await s.ResetSessions(id, r.Version, ct); return Results.NoContent(); });
app.MapPost("/api/tasks/{id:guid}/dependencies", async (Guid id, DependencyRequest r, TaskService s, CancellationToken ct) => { await s.SetDependencies(id, r, ct); return Results.NoContent(); });
app.MapGet("/api/task-options", async (Database db, CancellationToken ct) => await db.Tasks.AsNoTracking().OrderByDescending(t => t.CreatedAt).Select(t => new { t.Id, t.BoardId, t.Title, t.Stage }).ToArrayAsync(ct));
app.MapPost("/api/tasks/{id:guid}/cancel", async (Guid id, VersionRequest r, TaskService s, RunRegistry registry, CancellationToken ct) =>
{
    var task = await s.Find(id, ct); TaskService.CheckVersion(task, r.Version);
    if (task.ActiveRunId is not Guid runId) throw new RuleException("Активного запуска нет", 409);
    registry.Cancel(runId);
    // Active processes release their assignment only after their cancellation/exit is observed.
    if (!registry.Contains(runId))
        await s.FailRun(runId, "Запуск отменён пользователем. Перед повтором проверьте внешние действия.", RunState.Cancelled, ct);
    return Results.NoContent();
});
app.MapGet("/api/tasks/{id:guid}/artifact", (Guid id, string path, Workspace workspace) => Results.File(workspace.ResolveArtifact(id, path), "application/octet-stream", Path.GetFileName(path)));

app.MapGet("/api/schedules", async (string boardId, Database db, CancellationToken ct) => await db.Schedules.AsNoTracking().Where(s => s.BoardId == boardId).OrderBy(s => s.NextAt).ToArrayAsync(ct));
app.MapPost("/api/schedules", async (ScheduleRequest r, Scheduler scheduler, CancellationToken ct) => Results.Ok(await scheduler.Save(r, null, null, ct)));
app.MapPut("/api/schedules/{id:guid}", async (Guid id, long version, ScheduleRequest r, Scheduler scheduler, CancellationToken ct) => Results.Ok(await scheduler.Save(r, id, version, ct)));
app.MapPost("/api/schedules/preview", (ScheduleRequest r, TimeProvider clock) =>
{
    ScheduleMath.Validate(r);
    var s = new Recurrence { IntervalHours = r.DaysInterval * 24 + r.HoursInterval, Days = r.Days, StartAt = r.StartAt.ToUniversalTime(), TimeZone = r.TimeZone };
    var slots = new List<DateTimeOffset>(); var at = clock.GetUtcNow();
    for (var i = 0; i < 5; i++) { at = ScheduleMath.Next(s, at, i == 0); slots.Add(at); }
    return Results.Ok(slots);
});
app.MapPost("/api/schedules/{id:guid}/toggle", async (Guid id, VersionRequest r, Database db, TaskService service, TimeProvider clock, CancellationToken ct) =>
{
    var boardId = await db.Schedules.Where(s => s.Id == id).Select(s => s.BoardId).SingleOrDefaultAsync(ct) ?? throw new RuleException("Расписание не найдено", 404);
    await using var tx = await db.Database.BeginTransactionAsync(ct); await service.LockBoard(boardId, ct);
    var s = await db.Schedules.SingleAsync(s => s.Id == id, ct);
    if (s.Version != r.Version) throw new RuleException("Расписание изменилось", 409);
    s.Enabled = !s.Enabled; s.Version++;
    if (s.Enabled) s.NextAt = ScheduleMath.Next(s, clock.GetUtcNow(), true);
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.NoContent();
});
app.MapGet("/api/schedules/{id:guid}/history", async (Guid id, Database db, CancellationToken ct) => await db.Slots.AsNoTracking().Where(s => s.ScheduleId == id).OrderByDescending(s => s.At).Take(100).ToArrayAsync(ct));
app.MapGet("/api/runtime", async (RuntimeControl control, Microsoft.Extensions.Options.IOptions<RuntimeOptions> options, Database db, CancellationToken ct) => new { mode = control.Mode, executable = options.Value.Executable, publicUrl = options.Value.PublicUrl, wallTimeSeconds = options.Value.WallTimeSeconds, activeAgents = await db.Runs.CountAsync(r => r.State == RunState.Running, ct), agentLimit = options.Value.MaxConcurrentAgents });
app.MapGet("/api/office", OfficeSnapshot.Read);
app.MapPut("/api/runtime", (RuntimeMode request, RuntimeControl control) => { control.Set(request.Mode); return Results.Ok(new { mode = control.Mode }); });
app.MapGet("/api/events", async (HttpContext http, CancellationToken ct) =>
{
    http.Response.ContentType = "text/event-stream"; http.Response.Headers.CacheControl = "no-cache";
    try
    {
        while (!ct.IsCancellationRequested)
        {
            await http.Response.WriteAsync("event: refresh\ndata: {}\n\n", ct); await http.Response.Body.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
});
app.MapPost("/mcp", McpEndpoint.Handle);
app.MapMethods("/mcp", ["GET", "DELETE"], () => Results.StatusCode(405));

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Database>();
    if (args.Contains("--print-schema")) { Console.WriteLine(db.Database.GenerateCreateScript()); return; }
    await db.Database.MigrateAsync();
    foreach (var (id, name) in new[] { ("analytics", "Аналитика"), ("frontend", "Фронтенд"), ("backend", "Бэкенд"), ("testing", "Тестирование") })
    {
        var existing = await db.Boards.FindAsync(id);
        if (existing is null) db.Boards.Add(new Board { Id = id, Name = name,
            ExecutorInstructions = RoleInstructions.Executor(id), CriticInstructions = RoleInstructions.Critic(id) });
        else if (existing.Version == 0 && existing.ExecutorInstructions.Length == 0 && existing.CriticInstructions.Length == 0)
        {
            existing.ExecutorInstructions = RoleInstructions.Executor(id);
            existing.CriticInstructions = RoleInstructions.Critic(id);
        }
    }
    await db.SaveChangesAsync();
}
await app.RunAsync();

public sealed record RuntimeMode(string Mode);
public partial class Program;
