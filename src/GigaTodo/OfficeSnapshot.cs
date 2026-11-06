using Microsoft.EntityFrameworkCore;

namespace GigaTodo;

// Read-only projection. Never expose run credentials, prompts or CLI transcript as office metadata.
public static class OfficeSnapshot
{
    public static async Task<object> Read(Database db, TimeProvider clock, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddSeconds(-45);
        var query = from run in db.Runs.AsNoTracking()
                            join task in db.Tasks.AsNoTracking() on run.TaskId equals task.Id
                            select new
                            {
                                run.Id, run.TaskId, task.Title, task.BoardId, run.Role, run.State,
                                run.StartedAt, run.FinishedAt, run.Runner,
                                NeedsAttention = task.Blocker != null,
                                WaitingForReview = task.HumanReview && task.Stage == Stage.InReview
                            };
        // One SQL statement / MVCC snapshot: a run cannot appear both as active
        // and completed if it exits while this request is being served.
        var active = query.Where(r => r.State == RunState.Running).OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id).Take(100);
        var recent = query.Where(r => r.State != RunState.Running && r.FinishedAt >= cutoff).OrderByDescending(r => r.FinishedAt).ThenBy(r => r.Id).Take(20);
        var agents = await active.Concat(recent).OrderByDescending(r => r.State == RunState.Running).ThenByDescending(r => r.StartedAt).ThenBy(r => r.Id).ToArrayAsync(ct);
        return new { at = clock.GetUtcNow(), agents };
    }
}
