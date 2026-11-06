using Microsoft.EntityFrameworkCore;

namespace GigaTodo;

public static class ScheduleMath
{
    public static void Validate(ScheduleRequest r)
    {
        TaskService.Validate(new(r.Prompt, Links: r.Links));
        if (r.DaysInterval < 0 || r.HoursInterval < 0 || r.DaysInterval > 3650 || r.HoursInterval > 87600 || r.DaysInterval * 24 + r.HoursInterval < 1)
            throw new RuleException("Задайте положительный интервал, максимум 10 лет");
        if (r.Days.Length == 0 || r.Days.Any(d => d < 0 || d > 6)) throw new RuleException("Выберите дни недели");
        try { TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone); }
        catch (TimeZoneNotFoundException) { throw new RuleException("Неизвестный часовой пояс"); }
    }

    public static DateTimeOffset Next(Recurrence r, DateTimeOffset from, bool inclusive = false)
    {
        var step = TimeSpan.FromHours(r.IntervalHours).Ticks;
        var n = Math.Max(0L, (from.UtcTicks - r.StartAt.UtcTicks) / step);
        var candidate = r.StartAt.AddTicks(n * step);
        if (candidate < from || (!inclusive && candidate == from)) candidate = candidate.AddTicks(step);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone);
        // Integer hours repeat their weekday/time pattern in at most 168 steps.
        for (var i = 0; i < 336; i++, candidate = candidate.AddTicks(step))
            if (r.Days.Contains((int)TimeZoneInfo.ConvertTime(candidate, zone).DayOfWeek)) return candidate.ToUniversalTime();
        throw new RuleException("Интервал и дни недели не дают допустимых запусков");
    }
}

public sealed class Scheduler(Database db, TaskService service, TimeProvider clock)
{
    public async Task<Recurrence> Save(ScheduleRequest r, Guid? id, long? version, CancellationToken ct)
    {
        ScheduleMath.Validate(r);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(745018234)", ct);
        var board = await service.LockBoard(r.BoardId, ct);
        await service.ValidateDependencies(Guid.Empty, r.DependencyIds ?? [], ct);
        await service.ValidateAttachments(r.AttachmentIds ?? [], ct);
        var schedule = id is null ? new Recurrence() : await db.Schedules.FindAsync([id], ct) ?? throw new RuleException("Расписание не найдено", 404);
        if (id is not null && (schedule.Version != version || schedule.BoardId != r.BoardId)) throw new RuleException("Расписание изменилось", 409);
        schedule.BoardId = board.Id; schedule.Title = Data.Title(r.Prompt, r.Title); schedule.Prompt = Data.Normalize(r.Prompt);
        schedule.IntervalHours = r.DaysInterval * 24 + r.HoursInterval; schedule.Days = r.Days.Distinct().ToArray();
        schedule.StartAt = r.StartAt.ToUniversalTime(); schedule.TimeZone = r.TimeZone;
        schedule.DependencyIds = (r.DependencyIds ?? []).Distinct().ToArray(); schedule.HumanReview = r.HumanReview; schedule.Criteria = r.Criteria; schedule.Source = r.Source;
        schedule.AttachmentIds = (r.AttachmentIds ?? []).Distinct().ToArray();
        schedule.RepositoriesJson = r.Repositories is null ? board.DefaultRepositoriesJson : Data.Pack(r.Repositories);
        schedule.LinksJson = Data.Pack(r.Links ?? []);
        schedule.NextAt = ScheduleMath.Next(schedule, clock.GetUtcNow(), true); schedule.Version++;
        if (id is null) db.Schedules.Add(schedule);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return schedule;
    }

    public async Task Tick(bool recovery, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ids = await db.Schedules.AsNoTracking().Where(s => s.Enabled && s.NextAt <= now).Select(s => s.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var boardId = await db.Schedules.Where(s => s.Id == id).Select(s => s.BoardId).SingleAsync(ct);
            var board = await service.LockBoard(boardId, ct);
            var s = await db.Schedules.SingleAsync(s => s.Id == id, ct);
            if (!s.Enabled || s.NextAt > now) continue;
            // At most one timely slot per tick; stale ranges are represented by a skipped marker.
            var stale = recovery || now - s.NextAt > TimeSpan.FromMinutes(1);
            var slot = new ScheduleSlot { ScheduleId = s.Id, At = s.NextAt, State = stale ? "skipped" : "created" };
            if (!stale)
            {
                var task = service.Make(new(s.Prompt, s.BoardId, s.Title, s.Criteria, s.HumanReview, s.Source,
                    Data.Unpack<RepositorySpec[]>(s.RepositoriesJson), Data.Unpack<LinkSpec[]>(s.LinksJson), s.DependencyIds, s.AttachmentIds), board);
                task.ScheduleId = s.Id; task.ScheduledAt = s.NextAt; task.Position = await service.Bottom(board.Id, Stage.ToDo, ct);
                db.Tasks.Add(task); slot.TaskId = task.Id;
                service.Event(task, "scheduled", $"Экземпляр расписания {s.Id}; слот {s.NextAt:O}");
            }
            db.Slots.Add(slot);
            s.NextAt = ScheduleMath.Next(s, now); s.Version++;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
    }
}
