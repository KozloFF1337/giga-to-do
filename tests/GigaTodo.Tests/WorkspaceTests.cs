using System.Diagnostics;
using GigaTodo;
using Microsoft.Extensions.Options;
using Xunit;

namespace GigaTodo.Tests;

public class WorkspaceTests
{
    [Fact]
    public async Task TaskWorktreesAreIsolatedAndReusedWithoutChangingSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "giga-worktree-test-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "source"); Directory.CreateDirectory(repo);
        await Git(repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "original");
        await Git(repo, "add", "README.md");
        await Git(repo, "-c", "user.name=Giga test", "-c", "user.email=giga-test@example.invalid", "commit", "-m", "test fixture");
        var workspace = new Workspace(Options.Create(new RuntimeOptions { Root = Path.Combine(root, "runtime") }));
        var first = new WorkItem { RepositoriesJson = Data.Pack(new[] { new RepositorySpec(repo, "main") }) };
        var second = new WorkItem { RepositoriesJson = first.RepositoriesJson };
        var paths1 = await workspace.PrepareRepositories(first, default);
        var paths2 = await workspace.PrepareRepositories(second, default);
        Assert.NotEqual(paths1[0], paths2[0]);
        await File.WriteAllTextAsync(Path.Combine(paths1[0], "README.md"), "changed");
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(repo, "README.md")));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(paths2[0], "README.md")));
        Assert.Equal(paths1, await workspace.PrepareRepositories(first, default));
        // The executor discovers Jira type through its tools and renames its own
        // branch. Critic/rework/resume must reuse that worktree afterwards.
        first.LinksJson = Data.Pack(new[] { new LinkSpec("Story", "https://jira.example.invalid/browse/TEAM-123") });
        await Git(paths1[0], "branch", "-m", "feature/TEAM-123");
        Assert.Equal(paths1, await workspace.PrepareRepositories(first, default));
        await Git(paths1[0], "branch", "-m", "bugfix/TEAM-123");
        Assert.Equal(paths1, await workspace.PrepareRepositories(first, default));
        await Git(paths1[0], "branch", "-m", "feature/OTHER-9");
        await Assert.ThrowsAsync<RuleException>(() => workspace.PrepareRepositories(first, default));
        await Git(paths1[0], "switch", "--detach");
        await Assert.ThrowsAsync<RuleException>(() => workspace.PrepareRepositories(first, default));
    }

    [Fact]
    public async Task ArtifactDownloadRejectsTraversalAndConfigFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "giga-artifact-test-" + Guid.NewGuid().ToString("N"));
        var workspace = new Workspace(Options.Create(new RuntimeOptions { Root = root })); var id = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(workspace.TaskRoot(id), "executor", ".gigacode"));
        await File.WriteAllTextAsync(Path.Combine(workspace.TaskRoot(id), "executor", ".gigacode", "settings.json"), "secret");
        await File.WriteAllTextAsync(Path.Combine(workspace.TaskRoot(id), "report.txt"), "report");
        Assert.EndsWith("report.txt", workspace.ResolveArtifact(id, "report.txt"));
        Assert.Throws<RuleException>(() => workspace.ResolveArtifact(id, "executor/.gigacode/settings.json"));
        Assert.Throws<RuleException>(() => workspace.ResolveArtifact(id, "../../outside"));
    }

    [Fact]
    public void LogsHideKnownCredentialsWithoutHidingSessionId()
    {
        const string text = "{\"session_id\":\"session\",\"headers\":{\"Authorization\":\"Bearer external-secret\"},\"text\":\"own-token password=another-secret\"}";
        var clean = LogRedactor.Redact(text, "own-token");
        Assert.DoesNotContain("external-secret", clean); Assert.DoesNotContain("own-token", clean); Assert.DoesNotContain("another-secret", clean); Assert.Contains("session", clean);
    }

    private static async Task Git(string cwd, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await output;
        Assert.True(process.ExitCode == 0, await error);
    }
}
