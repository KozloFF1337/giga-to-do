using System.Text;
using System.Text.Json;
using GigaTodo;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GigaTodo.Tests;

[Collection("Postgres")]
public class McpTests : PostgresTestBase
{
    private static Database Open() => new(new DbContextOptionsBuilder<Database>().UseNpgsql(Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE")).Options);
    private static async Task<Assignment> Run()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var board = new Board { Id = Guid.NewGuid().ToString("N"), Name = "MCP integration" }; db.Boards.Add(board); await db.SaveChangesAsync();
        var clock = new TestClock(); var service = new TaskService(db, clock);
        await service.Create(new("MCP parent", board.Id));
        return (await new Dispatcher(db, service, clock).Claim(board.Id, false, new RuntimeOptions { Runner = "Mock" }, default))!;
    }
    private static async Task<JsonElement> Call(Assignment run, string method, object? parameters = null)
    {
        await using var db = Open(); var service = new TaskService(db, new TestClock());
        var http = new DefaultHttpContext(); http.Request.Headers.Authorization = "Bearer " + run.Token;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Data.Pack(new { jsonrpc = "2.0", id = 1, method, @params = parameters })));
        var workspace = new Workspace(Microsoft.Extensions.Options.Options.Create(new RuntimeOptions()));
        var store = new AttachmentStore(workspace, Microsoft.Extensions.Options.Options.Create(new AttachmentOptions()), new TestClock());
        var result = await McpEndpoint.Handle(http, db, service, store, default);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonSerializer.SerializeToElement(value, Data.Json);
    }
    private static JsonElement Output(JsonElement rpc)
    {
        Assert.False(rpc.GetProperty("result").GetProperty("isError").GetBoolean());
        return JsonDocument.Parse(rpc.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    [PostgresFact]
    public async Task InitializeToolsCreateAndSubmissionUseActualProtocol()
    {
        var run = await Run();
        var init = await Call(run, "initialize", new { protocolVersion = "2025-03-26" });
        Assert.Equal("2025-03-26", init.GetProperty("result").GetProperty("protocolVersion").GetString());
        var tools = await Call(run, "tools/list");
        Assert.Equal(McpEndpoint.ToolNames.Length, tools.GetProperty("result").GetProperty("tools").GetArrayLength());
        var child = Output(await Call(run, "tools/call", new { name = "create_task", arguments = new { prompt = "MCP child" } }));
        Assert.True(child.GetProperty("created").GetBoolean());
        Assert.Equal(run.TaskId.ToString(), child.GetProperty("task").GetProperty("parentId").GetString());
        var progress = Output(await Call(run, "tools/call", new { name = "report_progress", arguments = new { text = "Проверяю" } }));
        Assert.True(progress.GetProperty("saved").GetBoolean());
        var result = Output(await Call(run, "tools/call", new { name = "submit_result", arguments = new { outcome = "completed", summary = "Результат передан" } }));
        Assert.True(result.GetProperty("submitted").GetBoolean());
        await Assert.ThrowsAsync<RuleException>(() => Call(run, "tools/list"));
        await using var db = Open(); Assert.Equal(Stage.InWork, (await db.Tasks.FindAsync(run.TaskId))!.Stage);
    }

    [PostgresFact]
    public async Task BrowserLeaseIsExclusiveAndReleasedAfterCancellation()
    {
        // This is an explicitly separate test database; remove a lease from a prior failed test.
        await using (var cleanup = Open()) await cleanup.Resources.ExecuteDeleteAsync();
        var first = await Run(); var second = await Run();
        try
        {
            var request = new { name = "acquire_resource", arguments = new { resource = "browser" } };
            Assert.True(Output(await Call(first, "tools/call", request)).GetProperty("acquired").GetBoolean());
            Assert.False(Output(await Call(second, "tools/call", request)).GetProperty("acquired").GetBoolean());
            await using (var db = Open()) await new TaskService(db, new TestClock()).FailRun(first.RunId, "Cancelled", RunState.Cancelled, default);
            Assert.True(Output(await Call(second, "tools/call", request)).GetProperty("acquired").GetBoolean());
            await Call(second, "tools/call", new { name = "release_resource", arguments = new { resource = "browser" } });
        }
        finally
        {
            foreach (var run in new[] { first, second })
            {
                await using var db = Open(); await new TaskService(db, new TestClock()).FailRun(run.RunId, "Test cleanup", RunState.Cancelled, default);
            }
        }
    }
}
