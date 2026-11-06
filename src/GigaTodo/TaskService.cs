using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace GigaTodo;

public sealed class TaskService(Database db, TimeProvider clock)
{
    public async Task<Board> LockBoard(string id, CancellationToken ct = default) =>
        await db.Boards.FromSqlInterpolated($"SELECT * FROM boards WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
        ?? throw new RuleException("Доска не найдена", 404);

    public async Task<WorkItem> Find(Guid id, CancellationToken ct = default) =>
        await db.Tasks.FindAsync([id], ct) ?? throw new RuleException("Задача не найдена", 404);

    public void Event(WorkItem task, string kind, string text, string author = "system", Guid? run = null) =>
        db.Events.Add(new TaskEvent { TaskId = task.Id, RunId = run, Kind = kind, Text = text, Author = author, At = clock.GetUtcNow() });

    public static void CheckVersion(WorkItem t, long version)
    {
        if (t.Version != version) throw new RuleException("Задача изменилась. Обновите окно.", 409);
    }

    public static void Validate(CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 100_000) throw new RuleException("Описание обязательно, максимум 100000 символов");
        ValidateLinks(request.Links ?? []);
        if ((request.Repositories?.Length ?? 0) > 10) throw new RuleException("Максимум 10 репозиториев");
    }

    public static void ValidateLinks(IEnumerable<LinkSpec> links)
    {
        foreach (var link in links)
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new RuleException("Ссылка должна использовать http или https");
    }

    public async Task<(WorkItem Task, bool Created)> Create(CreateTaskRequest request, Guid? parent = null, Guid? creator = null, CancellationToken ct = default)
    {
        Validate(request);
        var boardId = request.BoardId ?? "analytics";
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(745018234)", ct);
        var locks = new HashSet<string> { boardId };
        if (creator is Guid creatorId)
        {
            var creatorTask = await db.Runs.AsNoTracking().Where(r => r.Id == creatorId).Select(r => r.TaskId).SingleOrDefaultAsync(ct);
            if (creatorTask != parent) throw new RuleException("Неверный родитель задачи", 403);
            locks.Add(await db.Tasks.Where(t => t.Id == creatorTask).Select(t => t.BoardId).SingleAsync(ct));
        }
        foreach (var id in locks.Order(StringComparer.Ordinal)) await LockBoard(id, ct);
        var board = await db.Boards.SingleAsync(b => b.Id == boardId, ct);
        if (creator is Guid authorId)
        {
            var author = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == authorId, ct);
            if (author.State != RunState.Running || author.PendingResultJson != null || author.ExpiresAt <= clock.GetUtcNow()) throw new RuleException("Запуск больше не активен", 401);
        }
        var key = Data.Key(boardId, request.Prompt, request.Source);
        var existing = await db.Tasks.SingleOrDefaultAsync(t => t.DedupeKey == key, ct);
        if (existing is not null)
        {
            // A deduplicated submission must not silently drop newly uploaded files.
            var added = (request.AttachmentIds ?? []).Except(existing.AttachmentIds).ToArray();
            if (added.Length > 0)
            {
                if (existing.ActiveRunId is not null) throw new RuleException("Такая задача уже выполняется. Прикрепите файлы после завершения запуска.", 409);
                var all = existing.AttachmentIds.Concat(added).ToArray(); await ValidateAttachments(all, ct);
                existing.AttachmentIds = all; existing.Version++; Event(existing, "attachments", "Добавлены вложения: " + string.Join(", ", added), creator is null ? "human" : "agent");
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct); return (existing, false);
        }
        await ValidateAttachments(request.AttachmentIds ?? [], ct);
        var task = Make(request, board);
        await ValidateDependencies(task.Id, task.DependencyIds, ct);
        task.ParentId = parent;
        task.CreatedByRunId = creator;
        task.DedupeKey = key;
        task.Position = await Bottom(boardId, Stage.ToDo, ct);
        db.Tasks.Add(task);
        Event(task, "created", "Задача добавлена в To Do", creator is null ? "human" : "agent", creator);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (task, true);
    }

    public WorkItem Make(CreateTaskRequest r, Board b) => new()
    {
        BoardId = b.Id, Prompt = Data.Normalize(r.Prompt), Title = Data.Title(r.Prompt, r.Title),
        Criteria = r.Criteria, Source = string.IsNullOrWhiteSpace(r.Source) ? null : Data.Normalize(r.Source),
        DependencyIds = (r.DependencyIds ?? []).Distinct().ToArray(), HumanReview = r.HumanReview,
        AttachmentIds = (r.AttachmentIds ?? []).Distinct().ToArray(),
        RepositoriesJson = r.Repositories is null ? b.DefaultRepositoriesJson : Data.Pack(r.Repositories),
        LinksJson = Data.Pack(r.Links ?? []), CreatedAt = clock.GetUtcNow()
    };

    public async Task<long> Bottom(string board, Stage stage, CancellationToken ct) =>
        (await db.Tasks.Where(t => t.BoardId == board && t.Stage == stage).MaxAsync(t => (long?)t.Position, ct) ?? 0) + 1024;

    public async Task ValidateAttachments(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length > 50) throw new RuleException("Максимум 50 вложений у задачи");
        var unique = ids.Distinct().ToArray();
        if (await db.Attachments.CountAsync(a => unique.Contains(a.Id), ct) != unique.Length) throw new RuleException("Одно из вложений не найдено");
    }

    public async Task SetAttachments(Guid id, AttachmentRequest request, CancellationToken ct)
    {
        var boardId = (await Find(id, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBoard(boardId, ct);
        var task = await Find(id, ct); CheckVersion(task, request.Version);
        if (task.ActiveRunId is not null) throw new RuleException("Вложения нельзя менять во время активного запуска. Дождитесь завершения или отмените его.", 409);
        var ids = request.AttachmentIds ?? [];
        await ValidateAttachments(ids, ct);
        task.AttachmentIds = ids.Distinct().ToArray(); task.Version++;
        Event(task, "attachments", "Обновлены вложения: " + string.Join(", ", task.AttachmentIds), "human");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task ValidateDependencies(Guid taskId, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length > 100 || ids.Contains(taskId)) throw new RuleException("Нельзя зависеть от себя; максимум 100 зависимостей");
        if (await db.Tasks.CountAsync(t => ids.Contains(t.Id), ct) != ids.Distinct().Count()) throw new RuleException("Одна из задач-зависимостей не найдена");
        var graph = await db.Tasks.AsNoTracking().Select(t => new { t.Id, t.DependencyIds }).ToDictionaryAsync(t => t.Id, t => t.DependencyIds, ct);
        var visited = new HashSet<Guid>(); var pending = new Stack<Guid>(ids);
        while (pending.TryPop(out var id))
        {
            if (id == taskId) throw new RuleException("Эти зависимости создают цикл");
            if (visited.Add(id) && graph.TryGetValue(id, out var next)) foreach (var item in next) pending.Push(item);
        }
    }

    public async Task SetDependencies(Guid id, DependencyRequest r, CancellationToken ct)
    {
        var boardId = (await Find(id, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(745018234)", ct);
        await LockBoard(boardId, ct);
        var task = await Find(id, ct); CheckVersion(task, r.Version);
        if (task.Stage != Stage.ToDo || task.ActiveRunId is not null) throw new RuleException("Зависимости меняются только в To Do", 409);
        await ValidateDependencies(id, r.DependencyIds, ct);
        task.DependencyIds = r.DependencyIds.Distinct().ToArray(); task.Version++;
        Event(task, "dependencies", "Обновлены зависимости: " + string.Join(", ", task.DependencyIds), "human");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task Reorder(ReorderRequest r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBoard(r.BoardId, ct);
        var tasks = await db.Tasks.Where(t => t.BoardId == r.BoardId && t.Stage == r.Stage).ToListAsync(ct);
        if (tasks.Count != r.Ids.Length || !tasks.Select(t => t.Id).ToHashSet().SetEquals(r.Ids))
            throw new RuleException("Состав колонки изменился. Обновите доску.", 409);
        for (var i = 0; i < r.Ids.Length; i++) { var t = tasks.Single(t => t.Id == r.Ids[i]); t.Position = (i + 1L) * 1024; t.Version++; }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task Pause(Guid id, long version, bool paused, CancellationToken ct)
    {
        var boardId = (await Find(id, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var board = await LockBoard(boardId, ct);
        var task = await Find(id, ct);
        CheckVersion(task, version);
        if (task.ActiveRunId is not null) throw new RuleException("Сначала отмените активный запуск", 409);
        task.Paused = paused;
        if (!paused) task.Blocker = null;
        task.Version++;
        Event(task, paused ? "paused" : "resumed", paused ? "Автоматическая обработка приостановлена" : "Обработка продолжена", "human");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task HumanReview(Guid id, ReviewRequest r, CancellationToken ct)
    {
        var boardId = (await Find(id, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var board = await LockBoard(boardId, ct);
        var task = await Find(id, ct);
        CheckVersion(task, r.Version);
        if (task.Stage != Stage.InReview || !task.HumanReview || task.ActiveRunId is not null)
            throw new RuleException("Задача не ожидает ручного ревью", 409);
        await ApplyReview(task, board, r.Accepted, r.Comment, "human", null, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ApplyReview(WorkItem task, Board board, bool accepted, string comment, string author, Guid? run, CancellationToken ct)
    {
        if (!accepted && string.IsNullOrWhiteSpace(comment)) throw new RuleException("Укажите замечания");
        task.Stage = accepted ? Stage.Done : Stage.ToDo;
        task.PendingRole = null;
        task.Paused = false;
        task.Blocker = null;
        if (!accepted)
        {
            task.Reworks++;
            task.Position = await Bottom(task.BoardId, Stage.ToDo, ct);
            if (task.Reworks >= board.ReworkLimit) { task.Paused = true; task.Blocker = "Достигнут лимит возвратов. Требуется решение человека."; }
        }
        task.Version++;
        Event(task, accepted ? "accepted" : "rework", comment, author, run);
    }

    public async Task Message(Guid id, MessageRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Text) || r.Text.Length > 50_000) throw new RuleException("Сообщение обязательно, максимум 50000 символов");
        var boardId = (await Find(id, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBoard(boardId, ct);
        var task = await Find(id, ct);
        CheckVersion(task, r.Version);
        if (task.ActiveRunId is not null) throw new RuleException("Дождитесь завершения или отмените запуск", 409);
        if (task.Stage == Stage.Done) throw new RuleException("Задача уже завершена", 409);
        if (r.Target is not ("Executor" or "Critic")) throw new RuleException("Неизвестный адресат");
        if (task.Stage == Stage.InReview && r.Target == "Critic" && task.HumanReview)
            throw new RuleException("Эту задачу проверяет человек");
        Event(task, "message", r.Text, "human → " + r.Target);
        task.Blocker = null;
        task.Paused = false;
        if (task.Stage == Stage.InReview) task.PendingRole = r.Target == "Executor" ? AgentRole.ExecutorReply : AgentRole.Critic;
        task.Version++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ResetSessions(Guid id, long version, CancellationToken ct)
    {
        var boardId = (await Find(id, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBoard(boardId, ct);
        var task = await Find(id, ct);
        CheckVersion(task, version);
        if (task.ActiveRunId is not null || task.Stage == Stage.Done) throw new RuleException("Сначала остановите активную работу", 409);
        task.ExecutorSession = null;
        task.CriticSession = null;
        task.Version++;
        Event(task, "session-reset", "ID сессий сброшены. Следующий запуск получит задание и историю из приложения; автоматический повтор не начат.", "human");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<AgentRun> Authenticate(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) throw new RuleException("Нужны полномочия запуска", 401);
        var hash = Data.Hash(token);
        var run = await db.Runs.SingleOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (run is null || run.State != RunState.Running || run.PendingResultJson is not null || run.ExpiresAt <= clock.GetUtcNow()) throw new RuleException("Полномочия истекли или отозваны", 401);
        return run;
    }

    public async Task Submit(Guid runId, TaskResult result, CancellationToken ct, bool defer = true)
    {
        if (string.IsNullOrWhiteSpace(result.Summary) || result.Summary.Length > 100_000) throw new RuleException("Укажите результат, максимум 100000 символов");
        ValidateLinks(result.Links ?? []);
        var original = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, ct);
        var boardId = (await Find(original.TaskId, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var board = await LockBoard(boardId, ct);
        var run = await db.Runs.SingleAsync(r => r.Id == runId, ct);
        var task = await Find(run.TaskId, ct);
        if (run.State != RunState.Running || run.ExpiresAt <= clock.GetUtcNow() || task.ActiveRunId != runId) throw new RuleException("Запуск больше не активен", 409);
        var outcome = result.Outcome;
        if (run.Role == AgentRole.Critic && task.Stage != Stage.InReview) throw new RuleException("Недопустимая стадия", 409);
        if (run.Role == AgentRole.Executor && task.Stage != Stage.InWork) throw new RuleException("Недопустимая стадия", 409);
        var valid = outcome is "error" or "clarification"
            || (outcome == "question" && task.Stage == Stage.InReview)
            || (outcome == "completed" && run.Role != AgentRole.Critic)
            || (run.Role == AgentRole.Critic && outcome is "accepted" or "rework");
        if (!valid) throw new RuleException("Итог не соответствует роли");
        if (defer)
        {
            if (run.PendingResultJson is not null) throw new RuleException("Результат уже передан", 409);
            run.PendingResultJson = Data.Pack(result);
            Event(task, "submitted", "Итог получен; ожидается завершение процесса", run.Role.ToString(), runId);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return;
        }
        if (outcome is "error" or "clarification")
        {
            task.Blocker = result.Summary;
            Event(task, outcome, result.Summary, run.Role.ToString(), runId);
        }
        else if (run.Role == AgentRole.Critic && outcome is "accepted" or "rework")
        {
            await ApplyReview(task, board, outcome == "accepted", result.Summary, "Critic", runId, ct);
        }
        else if (outcome == "question" && task.Stage == Stage.InReview)
        {
            Event(task, "message", result.Summary, run.Role.ToString(), runId);
            task.PendingRole = run.Role == AgentRole.Critic ? AgentRole.ExecutorReply : AgentRole.Critic;
        }
        else if (outcome == "completed" && run.Role != AgentRole.Critic)
        {
            if (run.Role == AgentRole.Executor)
            {
                task.ResultJson = Data.Pack(result);
                task.Stage = Stage.InReview;
                task.Position = await Bottom(task.BoardId, Stage.InReview, ct);
            }
            task.PendingRole = task.HumanReview ? null : AgentRole.Critic;
            Event(task, "result", result.Summary, run.Role.ToString(), runId);
        }
        else throw new RuleException("Итог не соответствует роли: completed / question / accepted / rework / clarification / error");
        task.ActiveRunId = null;
        task.Version++;
        run.State = outcome == "error" ? RunState.Failed : RunState.Succeeded;
        run.FinishedAt = clock.GetUtcNow();
        await db.Resources.Where(r => r.RunId == runId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task FailRun(Guid runId, string reason, RunState state, CancellationToken ct)
    {
        var original = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, ct);
        var boardId = (await Find(original.TaskId, ct)).BoardId;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBoard(boardId, ct);
        var run = await db.Runs.SingleAsync(r => r.Id == runId, ct);
        if (run.State == RunState.Running)
        {
            run.State = state; run.Error = reason; run.FinishedAt = clock.GetUtcNow();
            var task = await Find(run.TaskId, ct);
            if (task.ActiveRunId == runId) { task.ActiveRunId = null; task.Blocker = reason; task.Version++; }
            Event(task, "error", reason, "system", runId);
            await db.Resources.Where(r => r.RunId == runId).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
