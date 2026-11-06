using GigaTodo;
using Xunit;

namespace GigaTodo.Tests;

public sealed class BranchNamingTests
{
    [Theory]
    [InlineData("feature/TEAM-123", true)]
    [InlineData("bugfix/TEAM-123", true)]
    [InlineData("feature/TEAM-1234", false)]
    [InlineData("feature/TEAM-12", false)]
    [InlineData("bugfix/OTHER-123", false)]
    [InlineData("feature/TEAM-123-extra", false)]
    [InlineData("feature/'TEAM-123'", false)]
    [InlineData("main", false)]
    [InlineData("", false)]
    public void OnlyExactReferencedTicketBranchIsAccepted(string branch, bool expected)
    {
        var task = new WorkItem { Prompt = "Реализовать https://jira.example.invalid/browse/TEAM-123" };
        Assert.Equal(expected, BranchNaming.IsTaskBranch(task, branch, 0));
        Assert.True(BranchNaming.IsTaskBranch(task, $"giga/{task.Id:N}/r0", 0));
        Assert.False(BranchNaming.IsTaskBranch(task, $"giga/{task.Id:N}/r1", 0));
    }
}
