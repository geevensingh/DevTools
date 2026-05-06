namespace CopilotSessionMonitor.Core;

/// <summary>
/// In-memory representation of a single Copilot CLI session, kept up to date
/// by <see cref="SessionTailer"/>. Mutable; each property is updated on the
/// background thread and then projected onto the UI via the aggregator.
/// </summary>
public sealed class SessionState
{
    public required string SessionId { get; init; }
    public required string SessionDirectory { get; init; }

    // From workspace.yaml
    public string? Cwd { get; set; }
    public string? GitRoot { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Liveness
    public int? LockPid { get; set; }
    public bool LockFilePresent { get; set; }
    public bool PidAlive { get; set; }

    // Folded event-stream state
    public bool InFlightTurn { get; set; }
    public Dictionary<string, InFlightTool> InFlightTools { get; } = new();
    public string? BaseCommit { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public string? LastToolName { get; set; }
    public string? LastToolDescription { get; set; }
    public string? LastAskUserQuestion { get; set; }

    // Probed externally
    public bool GitDirty { get; set; }
    public DateTimeOffset? GitDirtyCheckedAt { get; set; }

    /// <summary>
    /// Set of titles the WT tab for this session has plausibly shown:
    /// the workspace summary, plus every <c>report_intent</c> the agent has
    /// fired in this session. Used for tab-matching in TerminalFocuser since
    /// Copilot mutates the OSC-2 title as it works. Normalized to lowercase
    /// and trimmed; bounded to recent entries.
    /// </summary>
    public HashSet<string> KnownTabTitles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output (completion) tokens consumed across all assistant messages
    /// in this session. <c>events.jsonl</c> doesn't carry input tokens — those
    /// only exist in the global <c>session-store.db</c> — so this is a partial
    /// usage signal, not full cost.</summary>
    public long OutputTokens { get; set; }

    /// <summary>Bounded queue of recent events for the timeline view. Newest at the back.</summary>
    public Queue<RecentEvent> RecentEvents { get; } = new();
    public const int RecentEventsCapacity = 20;

    /// <summary>When this session most recently transitioned into its current
    /// <see cref="DerivedStatus"/>. Set by the aggregator on each transition.</summary>
    public DateTimeOffset? EnteredStatusAt { get; set; }

    // Derived (set by aggregator before UI projection)
    public SessionStatus DerivedStatus { get; set; } = SessionStatus.Offline;

    /// <summary>Display title preferred for the row.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Summary) ? Summary! :
        !string.IsNullOrWhiteSpace(Name) ? Name! :
        SessionId;

    /// <summary>Repo identity for the meta line, e.g. "geevensingh/DevTools".</summary>
    public string DisplayRepo => Repository ?? (Cwd is null ? "(no repo)" : System.IO.Path.GetFileName(Cwd.TrimEnd('\\', '/')));

    public DateTimeOffset LastActivity =>
        LastEventAt ?? UpdatedAt ?? CreatedAt ?? DateTimeOffset.MinValue;
}

public sealed record InFlightTool(string ToolCallId, string ToolName, string? Description, DateTimeOffset StartedAt);

public sealed record RecentEvent(DateTimeOffset At, RecentEventKind Kind, string Text);

public enum RecentEventKind
{
    Tool,
    AssistantMessage,
    UserMessage,
    System,
}
