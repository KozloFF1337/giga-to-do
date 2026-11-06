using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GigaTodo;

public static class McpEndpoint
{
    public static readonly string[] ToolNames = ["list_boards", "list_tasks", "get_task", "create_task", "report_progress", "post_message", "request_clarification", "submit_result", "acquire_resource", "release_resource"];

    private static object Schema(object properties, string[]? required = null) => new { type = "object", properties, required = required ?? [], additionalProperties = false };
    private static object Text(string description) => new { type = "string", description };
    private static object Tool(string name, string description, object schema) => new { name, description, inputSchema = schema };
    private static readonly object[] Tools =
    [
        Tool("list_boards", "Получить четыре доски", Schema(new { })),
        Tool("acquire_resource", "Получить эксклюзивный доступ к общему браузеру до конца запуска", Schema(new { resource = new { type = "string", @enum = new[] { "browser" } } }, ["resource"])),
        Tool("release_resource", "Освободить общий браузер", Schema(new { resource = new { type = "string", @enum = new[] { "browser" } } }, ["resource"])),
        Tool("list_tasks", "Найти задачи; максимум 100 за запрос, доступна пагинация", Schema(new { boardId = Text("ID доски"), query = Text("Поиск в описании"), offset = new { type = "integer", minimum = 0 } })),
        Tool("get_task", "Прочитать задачу, результат и обсуждение. По умолчанию текущая", Schema(new { taskId = Text("UUID задачи") })),
        Tool("create_task", "Создать задачу в низ To Do на любой доске; вернёт существующую при совпадении", Schema(new
        {
            prompt = Text("Полное самостоятельное задание"), boardId = Text("ID доски, по умолчанию текущая"), title = Text("Название"), criteria = Text("Критерии"), source = Text("Необязательный идентификатор источника"),
            dependencyIds = new { type = "array", items = new { type = "string", format = "uuid" } }, humanReview = new { type = "boolean" },
            attachmentIds = new { type = "array", items = new { type = "string", format = "uuid" }, description = "ID уже загруженных вложений, например из текущей задачи" },
            links = new { type = "array", items = Schema(new { title = Text("Название"), url = Text("HTTP/HTTPS URL"), description = Text("Пояснение") }, ["title", "url"]) },
            repositories = new { type = "array", items = Schema(new { path = Text("Локальный репозиторий"), baseBranch = Text("Базовая ветка"), frontendCommand = Text("Команда запуска"), localUrl = Text("Локальный URL"), apiUrl = Text("API"), designUrl = Text("Дизайн") }, ["path"]) }
        }, ["prompt"])),
        Tool("report_progress", "Сохранить прогресс текущего запуска", Schema(new { text = Text("Прогресс") }, ["text"])),
        Tool("post_message", "Сообщение текущему обсуждению. Для передачи хода затем submit_result question", Schema(new { text = Text("Сообщение") }, ["text"])),
        Tool("request_clarification", "Завершить ход и запросить уточнение у человека", Schema(new { text = Text("Вопрос") }, ["text"])),
        Tool("submit_result", "Передать итог: executor completed; critic accepted/rework; question/clarification/error. После этого завершить процесс", Schema(new
        {
            outcome = new { type = "string", @enum = new[] { "completed", "accepted", "rework", "question", "clarification", "error" } },
            summary = Text("Результат, замечания или вопрос"),
            links = new { type = "array", items = Schema(new { title = Text("Название"), url = Text("HTTP/HTTPS URL"), description = Text("Пояснение") }, ["title", "url"]) },
            artifacts = new { type = "array", items = Schema(new { title = Text("Название"), path = Text("Путь относительно папки задачи") }, ["title", "path"]) },
            checks = new { type = "array", items = new { type = "string" } }
        }, ["outcome", "summary"]))
    ];

    public static async Task<IResult> Handle(HttpContext http, Database db, TaskService service, AttachmentStore store, CancellationToken ct)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
        var run = await service.Authenticate(token, ct);
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct); }
        catch (JsonException) { return Results.Json(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32700, message = "Parse error" } }); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(null, -32600, "Invalid request");
            var hasId = root.TryGetProperty("id", out var originalId);
            var id = hasId ? originalId.Clone() : default;
            if (!root.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String) return Error(hasId ? id : null, -32600, "Invalid request");
            var method = methodValue.GetString();
            if (!hasId) return Results.Accepted();
            try
            {
                object result;
                switch (method)
                {
                    case "initialize":
                        var requested = root.TryGetProperty("params", out var init) && init.TryGetProperty("protocolVersion", out var protocol) ? protocol.GetString() : "2025-03-26";
                        result = new { protocolVersion = requested is "2025-03-26" or "2025-06-18" or "2025-11-25" ? requested : "2025-03-26", capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "giga-todo", version = "1.0.0" } };
                        break;
                    case "ping": result = new { }; break;
                    case "tools/list": result = new { tools = Tools }; break;
                    case "tools/call":
                        var parameters = root.GetProperty("params");
                        var name = parameters.GetProperty("name").GetString() ?? "";
                        var args = parameters.TryGetProperty("arguments", out var argument) ? argument : JsonSerializer.SerializeToElement(new { });
                        var task = await service.Find(run.TaskId, ct);
                        object output;
                        switch (name)
                        {
                            case "list_boards": output = await db.Boards.AsNoTracking().Select(b => new { b.Id, b.Name }).ToArrayAsync(ct); break;
                            case "list_tasks":
                                var query = db.Tasks.AsNoTracking();
                                if (String(args, "boardId") is { } board) query = query.Where(t => t.BoardId == board);
                                if (String(args, "query") is { } search) query = query.Where(t => t.Prompt.Contains(search));
                                var offset = args.TryGetProperty("offset", out var off) ? Math.Max(0, off.GetInt32()) : 0;
                                output = await query.OrderByDescending(t => t.CreatedAt).Skip(offset).Take(100).Select(t => new { t.Id, t.BoardId, t.Title, t.Prompt, t.Stage, t.Source, t.Version }).ToArrayAsync(ct);
                                break;
                            case "get_task":
                                var target = String(args, "taskId") is { } taskId ? await service.Find(Guid.Parse(taskId), ct) : task;
                                output = new { task = target, attachments = await store.Describe(db, target.AttachmentIds, ct), history = await db.Events.AsNoTracking().Where(e => e.TaskId == target.Id && e.Kind != "log").OrderBy(e => e.Id).Take(500).ToArrayAsync(ct) };
                                break;
                            case "create_task":
                                var request = args.Deserialize<CreateTaskRequest>(Data.Json) ?? throw new RuleException("Не задана задача");
                                var created = await service.Create(request with { BoardId = request.BoardId ?? task.BoardId }, task.Id, run.Id, ct);
                                output = new { task = created.Task, created = created.Created };
                                break;
                            case "acquire_resource":
                            case "release_resource":
                                if (String(args, "resource") != "browser") throw new RuleException("Неизвестный ресурс");
                                db.ChangeTracker.Clear();
                                await using (var tx = await db.Database.BeginTransactionAsync(ct))
                                {
                                    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(745018232)", ct);
                                    var currentRun = await service.Authenticate(token, ct);
                                    var lease = await db.Resources.SingleOrDefaultAsync(r => r.Name == "browser", ct);
                                    if (name == "release_resource")
                                    {
                                        if (lease?.RunId == run.Id) db.Resources.Remove(lease);
                                        else if (lease is not null) throw new RuleException("Ресурс принадлежит другому запуску", 409);
                                        output = new { released = true };
                                    }
                                    else
                                    {
                                        var alive = lease is not null && lease.ExpiresAt > DateTimeOffset.UtcNow && await db.Runs.AnyAsync(r => r.Id == lease.RunId && r.State == RunState.Running, ct);
                                        if (alive && lease!.RunId != run.Id) output = new { acquired = false, reason = "Браузер занят другим запуском" };
                                        else
                                        {
                                            if (lease is null) { lease = new ResourceLease { Name = "browser" }; db.Resources.Add(lease); }
                                            lease.RunId = run.Id; lease.ExpiresAt = currentRun.ExpiresAt;
                                            output = new { acquired = true };
                                        }
                                    }
                                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                                }
                                break;
                            case "report_progress":
                            case "post_message":
                                var text = String(args, "text");
                                if (string.IsNullOrWhiteSpace(text) || text.Length > 50_000) throw new RuleException("Укажите текст, максимум 50000 символов");
                                // Recheck under the same board lock as finalization/cancellation.
                                db.ChangeTracker.Clear();
                                await using (var tx = await db.Database.BeginTransactionAsync(ct))
                                {
                                    await service.LockBoard(task.BoardId, ct);
                                    await service.Authenticate(token, ct);
                                    var current = await service.Find(task.Id, ct);
                                    if (current.ActiveRunId != run.Id) throw new RuleException("Запуск устарел", 409);
                                    service.Event(current, name == "post_message" ? "message" : "progress", text, run.Role.ToString(), run.Id);
                                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                                }
                                output = new { saved = true };
                                break;
                            case "request_clarification":
                                await service.Submit(run.Id, new("clarification", String(args, "text") ?? ""), ct);
                                output = new { submitted = true }; break;
                            case "submit_result":
                                var outcome = args.Deserialize<TaskResult>(Data.Json) ?? throw new RuleException("Не задан результат");
                                await service.Submit(run.Id, outcome, ct);
                                output = new { submitted = true }; break;
                            default: throw new RuleException("Неизвестный инструмент");
                        }
                        result = new { content = new[] { new { type = "text", text = Data.Pack(output) } }, isError = false };
                        break;
                    default: return Error(id, -32601, "Method not found");
                }
                return Results.Json(new { jsonrpc = "2.0", id, result }, Data.Json);
            }
            catch (Exception ex) when (ex is RuleException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return Results.Json(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text = ex.Message } }, isError = true } }, Data.Json);
            }
        }
    }

    private static string? String(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static IResult Error(JsonElement? id, int code, string message) => Results.Json(new { jsonrpc = "2.0", id, error = new { code, message } });
}
