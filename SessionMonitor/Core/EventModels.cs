using System.Text.Json.Serialization;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Minimal records mapping the fields of <c>events.jsonl</c> we actually use.
/// We intentionally do not model every field — additional properties are
/// ignored thanks to <see cref="JsonSerializerOptions"/> defaults.
/// </summary>
public sealed record EventEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp,
    [property: JsonPropertyName("data")] System.Text.Json.JsonElement? Data
);

public sealed record SessionStartData(
    [property: JsonPropertyName("startTime")] DateTimeOffset? StartTime
);

public sealed record SessionResumeContext(
    [property: JsonPropertyName("baseCommit")] string? BaseCommit,
    [property: JsonPropertyName("headCommit")] string? HeadCommit,
    [property: JsonPropertyName("cwd")] string? Cwd,
    [property: JsonPropertyName("branch")] string? Branch,
    [property: JsonPropertyName("repository")] string? Repository
);

public sealed record SessionResumeData(
    [property: JsonPropertyName("resumeTime")] DateTimeOffset? ResumeTime,
    [property: JsonPropertyName("context")] SessionResumeContext? Context
);

public sealed record AssistantMessageData(
    [property: JsonPropertyName("messageId")] string? MessageId,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("outputTokens")] long? OutputTokens
);

public sealed record UserMessageData(
    [property: JsonPropertyName("content")] string? Content
);

public sealed record ToolStartData(
    [property: JsonPropertyName("toolCallId")] string? ToolCallId,
    [property: JsonPropertyName("toolName")] string? ToolName,
    [property: JsonPropertyName("arguments")] System.Text.Json.JsonElement? Arguments
);

public sealed record ToolCompleteData(
    [property: JsonPropertyName("toolCallId")] string? ToolCallId,
    [property: JsonPropertyName("toolName")] string? ToolName
);
