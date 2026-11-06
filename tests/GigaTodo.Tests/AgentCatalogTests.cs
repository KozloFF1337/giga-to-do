using GigaTodo;
using Xunit;

namespace GigaTodo.Tests;

public class AgentCatalogTests
{
    [Fact]
    public void CriticAndReplyDoNotInheritAutoEditMode()
    {
        Assert.Equal("default", AgentCatalog.ApprovalMode(AgentRole.Critic, "auto-edit"));
        Assert.Equal("default", AgentCatalog.ApprovalMode(AgentRole.ExecutorReply, "auto"));
        Assert.Equal("auto-edit", AgentCatalog.ApprovalMode(AgentRole.Executor, "auto-edit"));
    }
    [Fact]
    public void CatalogContainsAllActiveSkillsAndEightProfiles()
    {
        var catalog = AgentCatalog.Default;
        Assert.Equal(32, catalog.Document.Skills.Length);
        Assert.Equal(8, catalog.Document.Profiles.Length);
        Assert.Equal(21, catalog.Document.Scenarios.Length);
        var used = catalog.Document.Scenarios.SelectMany(s => s.Required.Concat(s.Optional)).ToHashSet();
        Assert.All(catalog.Document.Skills, skill => Assert.Contains(skill.Id, used));
    }

    [Fact]
    public void CriticDoesNotReceiveImplementationOrExternalCommunicationScenarios()
    {
        var c = AgentCatalog.Default.Configuration("backend", AgentRole.Critic);
        Assert.Equal("critic", c.Policy.Id);
        Assert.Contains(c.Scenarios, s => s.Id == "critique");
        Assert.DoesNotContain(c.Scenarios, s => s.Id is "openspec-apply" or "archive" or "session-chat" or "analyst-chat" or "skill-maintenance");
        Assert.Contains("write_file", c.Policy.DisallowedTools);
    }

    [Fact]
    public void ReplyIsRestrictedToFeedbackAndRussianText()
    {
        var c = AgentCatalog.Default.Configuration("frontend", AgentRole.ExecutorReply);
        Assert.Equal("reply", c.Policy.Id);
        Assert.Equal(new[] { "feedback", "russian" }, c.Scenarios.Select(s => s.Id));
    }

    [Fact]
    public void SettingsHaveRolePermissionsAndRunScopedMcpWithoutGlobalTrust()
    {
        var c = AgentCatalog.Default.Configuration("backend", AgentRole.Critic);
        var text = Data.Pack(AgentCatalog.Settings(c, "http://localhost:5180/", "test-only"));
        Assert.Contains("permissions", text); Assert.Contains("Bearer test-only", text);
        Assert.DoesNotContain("trust", text);
        Assert.Contains("Bash(git push*)", text);
        Assert.DoesNotContain("Bash(*)", c.Policy.Ask);
    }

    [Fact]
    public void InstructionsPreserveConditionalChoiceAndSourceApprovals()
    {
        var c = AgentCatalog.Default.Configuration("analytics", AgentRole.Executor);
        var text = AgentCatalog.Instructions(c);
        Assert.Contains("не выполняй все сценарии подряд", text);
        Assert.Contains("openspec-context", text);
        Assert.Contains("request_clarification", text);
        Assert.Contains("Гейты полного исходника обязательны", text);
        Assert.Contains(c.CatalogHash, text);
    }
}
