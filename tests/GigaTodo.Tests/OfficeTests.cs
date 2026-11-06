using System.Text.Json;
using GigaTodo;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GigaTodo.Tests;

[Collection("Postgres")]
public sealed class OfficeTests : PostgresTestBase
{
    [PostgresFact]
    public async Task RecentRunsNeverCrowdOutOneHundredActiveAgents()
    {
        await using var db = new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE")).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var clock = new TestClock { Now = DateTimeOffset.UtcNow.AddYears(1) };
        var board = new Board { Id = Guid.NewGuid().ToString("N"), Name = "Integration test" };
        var task = new WorkItem { BoardId = board.Id, Title = "Office capacity", Prompt = "Test" };
        db.Boards.Add(board); db.Tasks.Add(task);
        for (var i = 0; i < 130; i++) db.Runs.Add(new AgentRun
        {
            TaskId = task.Id, Role = i % 2 == 0 ? AgentRole.Executor : AgentRole.Critic,
            State = i < 100 ? RunState.Running : RunState.Succeeded,
            StartedAt = clock.Now.AddSeconds(-1000 + i), FinishedAt = i < 100 ? null : clock.Now.AddSeconds(-1),
            TokenHash = Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync();
        using var json = JsonDocument.Parse(Data.Pack(await OfficeSnapshot.Read(db, clock, default)));
        var agents = json.RootElement.GetProperty("agents").EnumerateArray().ToArray();
        Assert.Equal(120, agents.Length);
        Assert.Equal(100, agents.Count(a => a.GetProperty("state").GetString() == "Running"));
        Assert.Equal(120, agents.Select(a => a.GetProperty("id").GetGuid()).Distinct().Count());
    }

    [PostgresFact]
    public async Task SnapshotShowsRunningAndRecentRolesWithoutCredentialsOrTranscripts()
    {
        await using var db = new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE")).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var clock = new TestClock { Now = DateTimeOffset.UtcNow.AddYears(1) };
        var board = new Board { Id = Guid.NewGuid().ToString("N"), Name = "Integration test" };
        var task = new WorkItem { BoardId = board.Id, Title = "Office test", Prompt = "PRIVATE-PROMPT", Stage = Stage.InReview, HumanReview = true };
        db.Boards.Add(board); db.Tasks.Add(task);
        AgentRun Run(AgentRole role, RunState state, int seconds) => new()
        {
            TaskId = task.Id, Role = role, State = state,
            StartedAt = clock.Now.AddSeconds(-60), FinishedAt = state == RunState.Running ? null : clock.Now.AddSeconds(seconds),
            TokenHash = "PRIVATE-TOKEN-" + Guid.NewGuid(), ProfileJson = "PRIVATE-PROFILE", PendingResultJson = "PRIVATE-RESULT"
        };
        var running = Run(AgentRole.Critic, RunState.Running, 0);
        var recent = Run(AgentRole.ExecutorReply, RunState.Succeeded, -20);
        var old = Run(AgentRole.Executor, RunState.Failed, -46);
        db.Runs.AddRange(running, recent, old); await db.SaveChangesAsync();
        var text = Data.Pack(await OfficeSnapshot.Read(db, clock, default));
        using var json = JsonDocument.Parse(text);
        var own = json.RootElement.GetProperty("agents").EnumerateArray().Where(a => a.GetProperty("taskId").GetGuid() == task.Id).ToArray();
        Assert.Equal(2, own.Length);
        Assert.Contains(own, a => a.GetProperty("id").GetGuid() == running.Id && a.GetProperty("role").GetString() == "Critic");
        Assert.Contains(own, a => a.GetProperty("id").GetGuid() == recent.Id && a.GetProperty("waitingForReview").GetBoolean());
        Assert.DoesNotContain("PRIVATE-", text);
        clock.Now = clock.Now.AddSeconds(30);
        using var later = JsonDocument.Parse(Data.Pack(await OfficeSnapshot.Read(db, clock, default)));
        Assert.Single(later.RootElement.GetProperty("agents").EnumerateArray(), a => a.GetProperty("taskId").GetGuid() == task.Id);
    }
}
