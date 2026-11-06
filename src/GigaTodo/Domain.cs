using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GigaTodo;

public enum Stage { ToDo, InWork, InReview, Done }
public enum AgentRole { Executor, Critic, ExecutorReply }
public enum RunState { Running, Succeeded, Failed, Interrupted, Cancelled }

public sealed class Board
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int ExecutorLimit { get; set; } = 2;
    public int CriticLimit { get; set; } = 1;
    public int ReworkLimit { get; set; } = 3;
    public string DefaultRepositoriesJson { get; set; } = "[]";
    public string ExecutorInstructions { get; set; } = "";
    public string CriticInstructions { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApprovalMode { get; set; } = "default";
    public long Version { get; set; }
}

public sealed class WorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BoardId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Criteria { get; set; } = "";
    public string? Source { get; set; }
    public string? DedupeKey { get; set; }
    public Stage Stage { get; set; }
    public long Position { get; set; }
    public Guid[] DependencyIds { get; set; } = [];
    public Guid[] AttachmentIds { get; set; } = [];
    public bool HumanReview { get; set; }
    public bool Paused { get; set; }
    public string? Blocker { get; set; }
    public int Reworks { get; set; }
    public long Version { get; set; } = 1;
    public Guid? ParentId { get; set; }
    public Guid? CreatedByRunId { get; set; }
    public Guid? ScheduleId { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public string RepositoriesJson { get; set; } = "[]";
    public string LinksJson { get; set; } = "[]";
    public string ResultJson { get; set; } = "{}";
    public string? ExecutorSession { get; set; }
    public string? CriticSession { get; set; }
    public AgentRole? PendingRole { get; set; }
    public Guid? ActiveRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public AgentRole Role { get; set; }
    public RunState State { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string? SessionId { get; set; }
    public string? Error { get; set; }
    public string Runner { get; set; } = "";
    public string ProfileJson { get; set; } = "{}";
    public string? PendingResultJson { get; set; }
}

public sealed class TaskEvent
{
    public long Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid? RunId { get; set; }
    public string Kind { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Recurrence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BoardId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
    public int IntervalHours { get; set; }
    public int[] Days { get; set; } = [1, 2, 3, 4, 5];
    public DateTimeOffset StartAt { get; set; }
    public string TimeZone { get; set; } = "Europe/Moscow";
    public DateTimeOffset NextAt { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid[] DependencyIds { get; set; } = [];
    public Guid[] AttachmentIds { get; set; } = [];
    public bool HumanReview { get; set; }
    public string RepositoriesJson { get; set; } = "[]";
    public string LinksJson { get; set; } = "[]";
    public string Criteria { get; set; } = "";
    public string? Source { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class ScheduleSlot
{
    public long Id { get; set; }
    public Guid ScheduleId { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? TaskId { get; set; }
    public string State { get; set; } = "";
}

public sealed class ResourceLease
{
    public string Name { get; set; } = "";
    public Guid RunId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed record RepositorySpec(string Path, string BaseBranch = "HEAD", string FrontendCommand = "", string LocalUrl = "", string ApiUrl = "", string DesignUrl = "");
public sealed record LinkSpec(string Title, string Url, string Description = "");
public sealed record ArtifactSpec(string Title, string Path);
public sealed record CreateTaskRequest(string Prompt, string? BoardId = null, string? Title = null, string Criteria = "", bool HumanReview = false, string? Source = null, RepositorySpec[]? Repositories = null, LinkSpec[]? Links = null, Guid[]? DependencyIds = null, Guid[]? AttachmentIds = null);
public sealed record AttachmentRequest(long Version, Guid[] AttachmentIds);
public sealed record TaskResult(string Outcome, string Summary, LinkSpec[]? Links = null, ArtifactSpec[]? Artifacts = null, string[]? Checks = null);
public sealed record DependencyRequest(long Version, Guid[] DependencyIds);
public sealed record VersionRequest(long Version);
public sealed record ReviewRequest(long Version, bool Accepted, string Comment);
public sealed record MessageRequest(long Version, string Text, string Target = "Executor");
public sealed record ReorderRequest(string BoardId, Stage Stage, Guid[] Ids);
public sealed record BoardSettings(long Version, int ExecutorLimit, int CriticLimit, int ReworkLimit, RepositorySpec[] DefaultRepositories, string ExecutorInstructions, string CriticInstructions, string Model, string ApprovalMode);
public sealed record ScheduleRequest(string BoardId, string Prompt, int DaysInterval, int HoursInterval, int[] Days, DateTimeOffset StartAt, string TimeZone = "Europe/Moscow", string? Title = null, bool HumanReview = false, RepositorySpec[]? Repositories = null, LinkSpec[]? Links = null, string Criteria = "", string? Source = null, Guid[]? DependencyIds = null, Guid[]? AttachmentIds = null);

public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public bool IsArchive { get; set; }
    public string State { get; set; } = "Ready";
    public string? Error { get; set; }
    public int ExtractedFiles { get; set; }
    public long ExtractedBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class Data
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static string Pack<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Unpack<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)!;
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Key(string board, string prompt, string? source) => Hash(Pack(new[] { board, Normalize(prompt), source is null ? "" : Normalize(source) }));
    public static string Title(string prompt, string? title) => string.IsNullOrWhiteSpace(title) ? Normalize(prompt).Split('\n')[0][..Math.Min(120, Normalize(prompt).Split('\n')[0].Length)] : title.Trim();
}

public sealed class RuleException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }
