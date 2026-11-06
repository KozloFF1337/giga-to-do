using GigaTodo;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GigaTodo.Tests;

[Collection("Postgres")]
public sealed class AttachmentPostgresTests : PostgresTestBase
{
    private static Database Open() => new(new DbContextOptionsBuilder<Database>().UseNpgsql(Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE")).Options);
    [PostgresFact]
    public async Task CreationDedupeAndChangesPreserveAttachmentsAndProtectActiveRuns()
    {
        await using var db = Open(); var clock = new TestClock(); var service = new TaskService(db, clock);
        var board = new Board { Id = Guid.NewGuid().ToString("N"), Name = "Integration test" };
        var first = new Attachment { FileName = "first.txt" };
        var second = new Attachment { FileName = "second.zip", IsArchive = true, State = "Extracted" };
        db.Boards.Add(board); db.Attachments.AddRange(first, second); await db.SaveChangesAsync();
        var task = (await service.Create(new("With attachments", board.Id, AttachmentIds: [first.Id]))).Task;
        Assert.Equal(new[] { first.Id }, task.AttachmentIds);
        var duplicate = await service.Create(new("With attachments", board.Id, AttachmentIds: [second.Id]));
        Assert.False(duplicate.Created); Assert.Equal(task.Id, duplicate.Task.Id);
        Assert.Equal(new[] { first.Id, second.Id }, duplicate.Task.AttachmentIds);
        await service.SetAttachments(task.Id, new(task.Version, [second.Id]), default);
        task = await service.Find(task.Id);
        Assert.Equal(new[] { second.Id }, task.AttachmentIds);
        await Assert.ThrowsAsync<RuleException>(() => service.Create(new("Missing attachment", board.Id, AttachmentIds: [Guid.NewGuid()])));
        db.ChangeTracker.Clear();
        var claim = await new Dispatcher(db, service, clock).Claim(board.Id, false, new RuntimeOptions { Runner = "Mock" }, default);
        Assert.NotNull(claim);
        var active = await service.Find(task.Id);
        var error = await Assert.ThrowsAsync<RuleException>(() => service.SetAttachments(task.Id, new(active.Version, []), default));
        Assert.Equal(409, error.Status);
        var profile = (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == claim.RunId)).ProfileJson;
        using var snapshot = System.Text.Json.JsonDocument.Parse(profile);
        Assert.Equal(second.Id, Assert.Single(snapshot.RootElement.GetProperty("attachmentIds").EnumerateArray()).GetGuid());
        await service.FailRun(claim.RunId, "test cleanup", RunState.Cancelled, default);
        await service.SetAttachments(task.Id, new((await service.Find(task.Id)).Version, [first.Id]), default);
        Assert.Equal(profile, (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == claim.RunId)).ProfileJson);
    }
    [PostgresFact]
    public async Task ScheduleCopiesAttachmentReferencesAtomicallyToEveryInstance()
    {
        await using var db = Open(); var clock = new TestClock(); var service = new TaskService(db, clock);
        var board = new Board { Id = Guid.NewGuid().ToString("N"), Name = "Integration test" };
        var file = new Attachment { FileName = "requirements.pdf" };
        db.Boards.Add(board); db.Attachments.Add(file); await db.SaveChangesAsync();
        var scheduler = new Scheduler(db, service, clock);
        var template = await scheduler.Save(new(board.Id, "Recurring with files", 0, 1, [0, 1, 2, 3, 4, 5, 6], clock.Now.AddHours(1), AttachmentIds: [file.Id]), null, null, default);
        clock.Now = clock.Now.AddHours(1); await scheduler.Tick(false, default);
        clock.Now = clock.Now.AddHours(1); await scheduler.Tick(false, default);
        var instances = await db.Tasks.Where(t => t.ScheduleId == template.Id).ToArrayAsync();
        Assert.Equal(2, instances.Length); Assert.All(instances, task => Assert.Equal(new[] { file.Id }, task.AttachmentIds));
    }
}
