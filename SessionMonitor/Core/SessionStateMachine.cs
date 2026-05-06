namespace CopilotSessionMonitor.Core;

/// <summary>
/// Pure-function classifier. Given a folded session state plus probe results,
/// decide which color the session should be. Kept side-effect-free so it can
/// be unit-tested over synthetic event sequences.
/// </summary>
public static class SessionStateMachine
{
    private static readonly HashSet<string> ChangeMakingTools = new(StringComparer.OrdinalIgnoreCase)
    {
        // Built-in CLI editing/creation tools we know about today.
        "edit", "create",
    };

    /// <summary>
    /// A session whose process is alive but whose events.jsonl hasn't been
    /// touched in this long is treated as abandoned (the user closed the
    /// terminal without quitting; the CLI is still parked at a prompt).
    /// We surface it as Offline so it falls under the "Show offline" filter.
    ///
    /// 4 hours is long enough that a normal "human went to lunch and came
    /// back" gap doesn't trigger it, but short enough that genuinely
    /// forgotten overnight sessions still drop out by morning. The previous
    /// 30 min default was too aggressive — sessions idling between user
    /// turns went offline mid-conversation.
    /// </summary>
    public static TimeSpan StaleThreshold { get; set; } = TimeSpan.FromHours(4);

    public static SessionStatus Classify(SessionState s) => Classify(s, DateTimeOffset.UtcNow);

    public static SessionStatus Classify(SessionState s, DateTimeOffset now)
    {
        // Liveness: no lock file, or lock present but PID dead -> offline.
        if (!s.LockFilePresent || s.LockPid is null || !s.PidAlive)
            return SessionStatus.Offline;

        // Stale: alive process but no event traffic for a while -> treat as offline.
        // Exception: never treat as stale while the agent is actively working —
        // an in-flight turn or tool is unambiguous proof of life regardless of
        // how long it has been since the last event was flushed.
        bool agentBusy = s.InFlightTurn || s.InFlightTools.Count > 0;
        if (!agentBusy && s.LastEventAt is { } last && now - last > StaleThreshold)
            return SessionStatus.Offline;

        // Blue: any in-flight ask_user beats everything else.
        foreach (var t in s.InFlightTools.Values)
        {
            if (string.Equals(t.ToolName, "ask_user", StringComparison.OrdinalIgnoreCase))
                return SessionStatus.Blue;
        }

        // Red: ONLY while a change-making tool is currently in flight. We
        // intentionally do NOT use GitDirty here — once the edit completes the
        // tree typically remains dirty until the user commits, but the agent
        // is no longer "actively making changes," it's idle. GitDirty is still
        // surfaced in the row preview/tooltip for context.
        foreach (var t in s.InFlightTools.Values)
        {
            if (ChangeMakingTools.Contains(t.ToolName))
                return SessionStatus.Red;
        }

        // Yellow: agent is busy (turn in flight or any non-ask/edit tool running).
        if (s.InFlightTurn || s.InFlightTools.Count > 0)
            return SessionStatus.Yellow;

        // Otherwise idle.
        return SessionStatus.Green;
    }
}
