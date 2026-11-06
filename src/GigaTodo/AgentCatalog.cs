using System.Text;

namespace GigaTodo;

public sealed record SkillDefinition(string Id, string Version, string Description, string Origin, string Availability);
public sealed record SkillScenario(string Id, string When, string[] Required, string[] Optional, string Gate, string Workflow, string[] Roles, string[] Boards);
public sealed record PermissionPolicy(string Id, string[] Allow, string[] Ask, string[] Deny, string[] DisallowedTools, string Instructions);
public sealed record AgentProfile(string Id, string BoardId, string Role, string Title, string PolicyId, string[] ScenarioIds);
public sealed record CompanionAgent(string Id, string Availability, string Description);
public sealed record CatalogDocument(string Version, SkillDefinition[] Skills, SkillScenario[] Scenarios, PermissionPolicy[] Policies, AgentProfile[] Profiles, CompanionAgent[] CompanionAgents);
public sealed record AgentConfiguration(string CatalogVersion, string CatalogHash, AgentProfile Profile, PermissionPolicy Policy, SkillScenario[] Scenarios, SkillDefinition[] Skills);

public sealed class AgentCatalog
{
    public CatalogDocument Document { get; }
    public string Hash { get; }
    public static AgentCatalog Default { get; } = new(Path.Combine(AppContext.BaseDirectory, "AgentConfig", "catalog.json"));

    public AgentCatalog(string path)
    {
        var json = File.ReadAllText(path);
        Document = Data.Unpack<CatalogDocument>(json);
        Hash = Data.Hash(json);
        Validate(Document);
    }

    public static void Validate(CatalogDocument d)
    {
        static void Unique(IEnumerable<string> ids) { var values = ids.ToArray(); if (values.Distinct().Count() != values.Length) throw new InvalidOperationException("Повтор ID в каталоге агентов"); }
        Unique(d.Skills.Select(s => s.Id)); Unique(d.Scenarios.Select(s => s.Id)); Unique(d.Profiles.Select(p => p.Id)); Unique(d.Policies.Select(p => p.Id));
        foreach (var s in d.Scenarios)
            if (s.Required.Concat(s.Optional).Any(id => !d.Skills.Any(k => k.Id == id))) throw new InvalidOperationException("Неизвестный навык: " + s.Id);
        foreach (var p in d.Profiles)
            if (!d.Policies.Any(v => v.Id == p.PolicyId) || p.ScenarioIds.Any(id => !d.Scenarios.Any(s => s.Id == id))) throw new InvalidOperationException("Некорректный профиль: " + p.Id);
    }

    public AgentConfiguration Configuration(string board, AgentRole role)
    {
        var original = Document.Profiles.SingleOrDefault(p => p.BoardId == board && p.Role == (role == AgentRole.Critic ? "Critic" : "Executor"));
        // Internal tests can create extra boards; they still get the generic protocol.
        var profile = original ?? Document.Profiles.First(p => p.BoardId == "analytics" && p.Role == (role == AgentRole.Critic ? "Critic" : "Executor"));
        var scenarios = Document.Scenarios.Where(s => s.Roles.Contains(role.ToString()) && (original is null || s.Boards.Contains(board))
            && (role == AgentRole.ExecutorReply || profile.ScenarioIds.Contains(s.Id))).ToArray();
        if (role == AgentRole.ExecutorReply) profile = profile with { Id = board + "-reply", Role = "ExecutorReply", Title = "Ответ исполнителя", PolicyId = "reply", ScenarioIds = scenarios.Select(s => s.Id).ToArray() };
        else profile = profile with { ScenarioIds = scenarios.Select(s => s.Id).ToArray() };
        var ids = scenarios.SelectMany(s => s.Required.Concat(s.Optional)).ToHashSet();
        return new(Document.Version, Hash, profile, Document.Policies.Single(p => p.Id == profile.PolicyId), scenarios, Document.Skills.Where(s => ids.Contains(s.Id)).ToArray());
    }

    public static string Instructions(AgentConfiguration c)
    {
        var text = new StringBuilder($"Профиль {c.Profile.Id}; каталог {c.CatalogVersion}, SHA256 {c.CatalogHash}.\n");
        text.AppendLine("Матрица условная: не выполняй все сценарии подряд. Выбери сценарии по фактической цели и структуре проекта, сообщи выбранные через report_progress. Для OpenSpec сначала openspec-context, один полный вход, без второго плана. Внешние навыки сначала обнаружить и прочитать; отсутствующий обязательный навык — request_clarification, а не имитация. Версии user — ожидаемые из реестра, не утверждение об установке.");
        text.AppendLine("Optional не означает автоматически запустить; применяй только по назначению. Явно порученные архив, внешняя переписка и изменение навыков не запускаются по одной лишь ссылке. Гейты полного исходника обязательны. Границы задачи и стадий имеют приоритет над полным циклом навыка.");
        foreach (var s in c.Scenarios)
            text.AppendLine($"[{s.Id}; {s.Workflow}] Когда: {s.When}. По порядку: {string.Join(" → ", s.Required)}. По необходимости: {string.Join(", ", s.Optional)}. Условия: {s.Gate}");
        text.AppendLine("Политика инструментов: " + c.Policy.Instructions);
        text.AppendLine("Сначала прочитай agent-profile.json и GIGACODE.md в текущей папке. Каталог не устанавливает пользовательские навыки и не разрешает скрытые инструменты. При несовпадении конфигурации и доступов не обходи ограничение.");
        return text.ToString();
    }

    public static object Settings(AgentConfiguration c, string url, string token) => new
    {
        permissions = new { allow = c.Policy.Allow, ask = c.Policy.Ask, deny = c.Policy.Deny },
        mcpServers = new Dictionary<string, object> { ["giga-todo"] = new
        {
            httpUrl = url.TrimEnd('/') + "/mcp", headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }, includeTools = McpEndpoint.ToolNames
        } }
    };

    public static string ApprovalMode(AgentRole role, string configured) => role == AgentRole.Executor ? configured : "default";
}
