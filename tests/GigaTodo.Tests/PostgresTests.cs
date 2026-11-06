using GigaTodo;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GigaTodo.Tests;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-09T07:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE"))) Skip = "Задайте GIGA_TEST_DATABASE для интеграционных тестов PostgreSQL"; }
}

[Collection("Postgres")]
public class PostgresTests : PostgresTestBase
{
    private static Database Open() => new(new DbContextOptionsBuilder<Database>().UseNpgsql(Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE")).Options);
    private static TaskService Service(Database db, TestClock? clock = null) => new(db, clock ?? new TestClock());
    private static async Task<string> NewBoard(int limit = 2)
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var id = Guid.NewGuid().ToString("N"); db.Boards.Add(new Board { Id = id, Name = "Integration test", ExecutorLimit = limit }); await db.SaveChangesAsync(); return id;
    }
    private static async Task<WorkItem> Add(string board, string prompt, bool human = false)
    {
        await using var db = Open(); return (await Service(db).Create(new(prompt, board, HumanReview: human))).Task;
    }
    private static async Task<Assignment?> Claim(string board, bool review = false, int globalLimit = 10)
    {
        await using var db = Open(); return await new Dispatcher(db, Service(db), new TestClock()).Claim(board, review, new RuntimeOptions { Runner = "Mock", MaxConcurrentAgents = globalLimit }, default);
    }
    private static async Task Complete(Assignment a, string outcome = "completed")
    {
        await using var db = Open(); var s = Service(db); var result = new TaskResult(outcome, "Проверено");
        await s.Submit(a.RunId, result, default); await s.Submit(a.RunId, result, default, defer: false);
    }

    [PostgresFact]
    public async Task ClaimPersistsCompleteAgentPolicyAndSkillMatrix()
    {
        var board = await NewBoard(); await Add(board, "Снимок профиля"); var run = (await Claim(board))!;
        await using var db = Open(); var saved = await db.Runs.FindAsync(run.RunId);
        using var json = System.Text.Json.JsonDocument.Parse(saved!.ProfileJson);
        var profile = json.RootElement.GetProperty("agentConfiguration").Deserialize<AgentConfiguration>(Data.Json)!;
        Assert.Equal(AgentCatalog.Default.Hash, profile.CatalogHash);
        Assert.Equal("executor", profile.Policy.Id);
        Assert.Contains(profile.Skills, s => s.Id == "task-execution");
    }

    [PostgresFact]
    public async Task ConcurrentCreateReturnsOneCard()
    {
        var board = await NewBoard();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ => { await using var db = Open(); return await Service(db).Create(new("Задача", board, Source: "T-1")); }));
        Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Task.Id).Distinct());
        await using var check = Open();
        Assert.Equal(2, (await Service(check).Create(new("Другая работа", board, Source: "T-1"))).Task.Position / 1024);
    }

    [PostgresFact]
    public async Task TopCardStartsFirst()
    {
        var board = await NewBoard(); var first = await Add(board, "Старая"); await Add(board, "Новая");
        Assert.Equal(first.Id, (await Claim(board))!.TaskId);
    }

    [PostgresFact]
    public async Task ConcurrentClaimsRespectLimitAndNeverDuplicate()
    {
        var board = await NewBoard(); for (var i = 0; i < 8; i++) await Add(board, "Работа " + i);
        var claims = (await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Claim(board)))).OfType<Assignment>().ToArray();
        Assert.Equal(2, claims.Length); Assert.Equal(2, claims.Select(c => c.TaskId).Distinct().Count());
    }

    [PostgresFact]
    public async Task BlockedTopTaskIsSkippedUntilAllDependenciesAreDone()
    {
        var board = await NewBoard(); var other = await NewBoard();
        var prerequisite = await Add(other, "Предварительная работа", human: true);
        var second = await Add(other, "Вторая предварительная работа", human: true);
        await using var db = Open(); var service = Service(db);
        var blocked = (await service.Create(new("Зависимая", board, DependencyIds: [prerequisite.Id, second.Id]))).Task;
        var ready = await Add(board, "Следующая доступная");
        Assert.Equal(ready.Id, (await Claim(board))!.TaskId);
        await Complete((await Claim(other))!);
        Assert.Null(await Claim(board)); // InReview is not Done.
        var current = await service.Find(prerequisite.Id);
        await service.HumanReview(current.Id, new(current.Version, true, "Готово"), default);
        Assert.Null(await Claim(board)); // The second prerequisite is still not Done.
        await Complete((await Claim(other))!);
        current = await service.Find(second.Id);
        await service.HumanReview(current.Id, new(current.Version, true, "Готово"), default);
        Assert.Equal(blocked.Id, (await Claim(board))!.TaskId);
    }

    [PostgresFact]
    public async Task DependencyCyclesAndChangesToRunningTasksAreRejected()
    {
        var board = await NewBoard(); var a = await Add(board, "A"); var b = await Add(board, "B");
        await using var db = Open(); var service = Service(db);
        await service.SetDependencies(a.Id, new(a.Version, [b.Id]), default);
        await Assert.ThrowsAsync<RuleException>(() => service.SetDependencies(b.Id, new(b.Version, [a.Id]), default));
        await Assert.ThrowsAsync<RuleException>(() => service.SetDependencies(b.Id, new(b.Version, [b.Id]), default));
        var running = (await Claim(board))!;
        var t = await service.Find(running.TaskId);
        await Assert.ThrowsAsync<RuleException>(() => service.SetDependencies(t.Id, new(t.Version, []), default));
    }

    [PostgresFact]
    public async Task ConcurrentDependencyEditsCannotCreateCycle()
    {
        var board = await NewBoard(); var a = await Add(board, "Цикл A"); var b = await Add(board, "Цикл B");
        async Task<bool> Edit(WorkItem task, Guid dependency)
        {
            await using var db = Open();
            try { await Service(db).SetDependencies(task.Id, new(task.Version, [dependency]), default); return true; }
            catch (RuleException) { return false; }
        }
        var results = await Task.WhenAll(Edit(a, b.Id), Edit(b, a.Id));
        Assert.Single(results, success => success);
    }

    [PostgresFact]
    public async Task CustomGlobalLimitIsUsedInsteadOfTen()
    {
        var first = await NewBoard(100); var second = await NewBoard(100);
        for (var i = 0; i < 14; i++) await Add(first, "Настраиваемый лимит " + i);
        await Add(second, "Другая доска");
        Assert.NotNull(await Claim(first, globalLimit: 1));
        Assert.Null(await Claim(second, globalLimit: 1));
        var more = (await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Claim(first, globalLimit: 12)))).OfType<Assignment>().ToArray();
        Assert.Equal(11, more.Length);
        Assert.Null(await Claim(second, globalLimit: 12));
    }

    [PostgresFact]
    public async Task GlobalLimitIsAtomicAcrossBoardsAndIncludesCritics()
    {
        var boards = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var board = await NewBoard(10); boards.Add(board);
            for (var j = 0; j < 8; j++) await Add(board, $"Работа {i}/{j}");
        }
        var completed = (await Claim(boards[0]))!; await Complete(completed);
        var critic = (await Claim(boards[0], true))!;
        var claims = (await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Claim(boards[i % 4])))).OfType<Assignment>().ToArray();
        Assert.Equal(9, claims.Length);
        Assert.Null(await Claim(boards[0]));
        await Complete(critic, "accepted");
        Assert.NotNull(await Claim(boards[0]));
    }

    [PostgresFact]
    public async Task SubmittedResultDoesNotReleaseSlotBeforeProcessExit()
    {
        var board = await NewBoard(1); await Add(board, "Первый"); var run = (await Claim(board))!;
        await using var db = Open(); var service = Service(db); var result = new TaskResult("completed", "Результат");
        await service.Submit(run.RunId, result, default);
        await Add(board, "Следующий"); Assert.Null(await Claim(board)); Assert.Null(await Claim(board, true));
        await Assert.ThrowsAsync<RuleException>(() => service.Authenticate(run.Token, default));
        await service.Submit(run.RunId, result, default, defer: false);
        Assert.NotNull(await Claim(board)); Assert.NotNull(await Claim(board, true));
    }

    [PostgresFact]
    public async Task HumanReviewIsNotClaimedAndStaleDecisionIsRejected()
    {
        var board = await NewBoard(); var task = await Add(board, "Проверить человеком", human: true); await Complete((await Claim(board))!);
        Assert.Null(await Claim(board, true));
        await using var db = Open(); var s = Service(db); var current = await s.Find(task.Id);
        await Assert.ThrowsAsync<RuleException>(() => s.HumanReview(task.Id, new(1, true, ""), default));
        await s.HumanReview(task.Id, new(current.Version, true, "Принято"), default);
        Assert.Equal(Stage.Done, (await s.Find(task.Id)).Stage);
    }

    [PostgresFact]
    public async Task CriticQuestionReplyAndReworkKeepHistory()
    {
        var board = await NewBoard(); var task = await Add(board, "Задача с обсуждением"); await Complete((await Claim(board))!);
        var critic = (await Claim(board, true))!; await Complete(critic, "question");
        var answer = (await Claim(board, true))!; Assert.Equal(AgentRole.ExecutorReply, answer.Role); await Complete(answer);
        var secondReview = (await Claim(board, true))!; Assert.Equal(AgentRole.Critic, secondReview.Role); await Complete(secondReview, "rework");
        await using var db = Open(); var current = await db.Tasks.FindAsync(task.Id);
        Assert.Equal(Stage.ToDo, current!.Stage); Assert.Equal(1, current.Reworks);
        Assert.True(await db.Events.CountAsync(e => e.TaskId == task.Id) >= 8);
    }

    [PostgresFact]
    public async Task CancelledRunCannotSubmit()
    {
        var board = await NewBoard(); await Add(board, "Задача"); var run = (await Claim(board))!;
        await using var db = Open(); var s = Service(db);
        await s.FailRun(run.RunId, "Отмена", RunState.Cancelled, default);
        await Assert.ThrowsAsync<RuleException>(() => s.Authenticate(run.Token, default));
        await Assert.ThrowsAsync<RuleException>(() => s.Submit(run.RunId, new("completed", "Поздний ответ"), default));
    }

    [PostgresFact]
    public async Task MissingSessionsCanBeResetWithoutAutomaticallyRetrying()
    {
        var board = await NewBoard(); await Add(board, "Восстановление сессии"); var run = (await Claim(board))!;
        await using var db = Open(); var s = Service(db);
        var task = await s.Find(run.TaskId);
        await Assert.ThrowsAsync<RuleException>(() => s.ResetSessions(task.Id, task.Version, default));
        await s.FailRun(run.RunId, "Сессия недоступна", RunState.Failed, default);
        task = await s.Find(run.TaskId); task.ExecutorSession = "missing"; task.CriticSession = "missing-critic"; await db.SaveChangesAsync();
        await s.ResetSessions(task.Id, task.Version, default);
        task = await s.Find(task.Id);
        Assert.Null(task.ExecutorSession); Assert.Null(task.CriticSession);
        Assert.NotNull(task.Blocker); Assert.Null(task.ActiveRunId);
        Assert.Null(await Claim(board));
    }

    [PostgresFact]
    public async Task ScheduleCreatesEachSlotEvenWithUnfinishedPreviousTask()
    {
        var board = await NewBoard(); var clock = new TestClock();
        await using var db = Open(); var scheduler = new Scheduler(db, Service(db, clock), clock);
        var r = await scheduler.Save(new(board, "Повтор", 0, 1, [0,1,2,3,4,5,6], clock.Now), null, null, default);
        await scheduler.Tick(false, default); await scheduler.Tick(false, default);
        clock.Now = clock.Now.AddHours(1); await scheduler.Tick(false, default);
        Assert.Equal(2, await db.Tasks.CountAsync(t => t.ScheduleId == r.Id));
        Assert.Equal(2, await db.Slots.CountAsync(s => s.ScheduleId == r.Id));
    }

    [PostgresFact]
    public async Task RecoverySkipsPastSlotsWithoutCreatingTasks()
    {
        var board = await NewBoard(); var clock = new TestClock();
        await using var db = Open(); var scheduler = new Scheduler(db, Service(db, clock), clock);
        var r = await scheduler.Save(new(board, "Повтор после простоя", 0, 1, [0,1,2,3,4,5,6], clock.Now), null, null, default);
        clock.Now = clock.Now.AddDays(2); await scheduler.Tick(true, default);
        Assert.Empty(await db.Tasks.Where(t => t.ScheduleId == r.Id).ToArrayAsync());
        Assert.Equal("skipped", (await db.Slots.SingleAsync(s => s.ScheduleId == r.Id)).State);
        Assert.True((await db.Schedules.FindAsync(r.Id))!.NextAt > clock.Now);
    }
}
