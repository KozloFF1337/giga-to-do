using Microsoft.EntityFrameworkCore;

namespace GigaTodo;

public sealed record Assignment(Guid RunId, Guid TaskId, string Token, AgentRole Role);

public sealed class Dispatcher(Database db, TaskService service, TimeProvider clock)
{
    public async Task<Assignment?> Claim(string boardId, bool review, RuntimeOptions options, CancellationToken ct)
    {
        if (!RuntimeOptions.ValidConcurrency(options)) throw new RuleException("MaxConcurrentAgents должен быть от 1 до 100");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(745018233)", ct);
        if (await db.Runs.CountAsync(r => r.State == RunState.Running, ct) >= options.MaxConcurrentAgents) return null;
        var board = await service.LockBoard(boardId, ct);
        var active = await db.Runs.CountAsync(r => r.State == RunState.Running && db.Tasks.Any(t => t.Id == r.TaskId && t.BoardId == boardId)
            && (review ? r.Role != AgentRole.Executor : r.Role == AgentRole.Executor), ct);
        if (active >= (review ? board.CriticLimit : board.ExecutorLimit)) return null;
        WorkItem? task;
        AgentRole role;
        if (review)
        {
            task = await db.Tasks.Where(t => t.BoardId == boardId && t.Stage == Stage.InReview && !t.Paused && t.Blocker == null && t.ActiveRunId == null
                && (!t.HumanReview || t.PendingRole == AgentRole.ExecutorReply))
                .OrderBy(t => t.Position).ThenBy(t => t.CreatedAt).FirstOrDefaultAsync(ct);
            role = task?.PendingRole == AgentRole.ExecutorReply ? AgentRole.ExecutorReply : AgentRole.Critic;
        }
        else
        {
            task = await db.Tasks.Where(t => t.BoardId == boardId && (t.Stage == Stage.ToDo || t.Stage == Stage.InWork)
                    && !t.Paused && t.Blocker == null && t.ActiveRunId == null
                    && !t.DependencyIds.Any(id => !db.Tasks.Any(p => p.Id == id && p.Stage == Stage.Done)))
                .OrderByDescending(t => t.Stage == Stage.InWork).ThenBy(t => t.Position).ThenBy(t => t.CreatedAt).FirstOrDefaultAsync(ct);
            role = AgentRole.Executor;
        }
        if (task is null || task.ActiveRunId != null || task.Paused || task.Blocker is not null) return null;
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var run = new AgentRun
        {
            TaskId = task.Id, Role = role, TokenHash = Data.Hash(token), ExpiresAt = clock.GetUtcNow().AddSeconds(options.WallTimeSeconds + 60),
            StartedAt = clock.GetUtcNow(), Runner = options.Runner,
            ProfileJson = Data.Pack(new { board.Model, board.ApprovalMode, board.Version, board.ExecutorInstructions, board.CriticInstructions, options.MaxTurns, options.MaxToolCalls, options.WallTimeSeconds, options.MaxConcurrentAgents,
                AgentConfiguration = AgentCatalog.Default.Configuration(board.Id, role),
                AttachmentIds = task.AttachmentIds,
                ExtensionVersion = "1.1.0", SuperpowersRevision = "22f0b463e7a5e579904dd6211a520321035526f7" })
        };
        db.Runs.Add(run);
        task.ActiveRunId = run.Id;
        if (!review) task.Stage = Stage.InWork;
        task.PendingRole = null;
        task.Version++;
        service.Event(task, "started", $"Запущен {role} ({options.Runner})", "system", run.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(run.Id, task.Id, token, role);
    }
}
