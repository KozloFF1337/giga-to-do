using System.Text.RegularExpressions;

namespace GigaTodo;

public static partial class BranchNaming
{
    // Jira itself stays behind the agent's tools. This validates only the shape and
    // reference to the task; the agent/critic must verify Story versus Bug in Jira.
    [GeneratedRegex(@"^(?:feature|bugfix)/([A-Z][A-Z0-9_]*-[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TicketBranch();

    public static bool IsTaskBranch(WorkItem task, string branch, int repositoryIndex)
    {
        if (branch == $"giga/{task.Id:N}/r{repositoryIndex}") return true;
        var match = TicketBranch().Match(branch);
        if (!match.Success) return false;
        var key = match.Groups[1].Value;
        var context = task.Prompt + "\n" + task.Source + "\n" + task.LinksJson;
        return Regex.IsMatch(context, @"(?<![A-Za-z0-9_-])" + Regex.Escape(key) + @"(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant);
    }
}
