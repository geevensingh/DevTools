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

    /// <summary>
    /// Upper bound on how long a session can appear "in-flight" (open turn,
    /// or unresolved <c>tool.execution_start</c>) before we override that
    /// signal and treat the session as crashed. Without this, a session that
    /// exited mid-tool — leaving a <c>tool.execution_start</c> with no
    /// matching <c>_complete</c> in <c>events.jsonl</c> — would be considered
    /// "agent busy" forever and never go Offline regardless of how long ago
    /// the orphaned event was written.
    ///
    /// 24 hours covers the worst legitimate long-running tool (overnight
    /// builds, big migrations, multi-hour test suites) while still catching
    /// genuinely abandoned sessions by the next day. Tweak here if needed.
    ///
    /// Effective threshold at runtime is
    /// <c>max(WorkingStaleConstant, StaleThreshold + 4h)</c> so that any
    /// future tweak of the idle threshold cannot accidentally make working
    /// less than idle and re-introduce the never-stale bug.
    /// </summary>
    public static TimeSpan WorkingStaleConstant { get; set; } = TimeSpan.FromHours(24);

    public static TimeSpan EffectiveWorkingStaleThreshold
    {
        get
        {
            var minBound = StaleThreshold + TimeSpan.FromHours(4);
            return WorkingStaleConstant >= minBound ? WorkingStaleConstant : minBound;
        }
    }

    public static SessionStatus Classify(SessionState s) => Classify(s, DateTimeOffset.UtcNow);

    public static SessionStatus Classify(SessionState s, DateTimeOffset now)
    {
        // Liveness: no lock file, or lock present but PID dead -> offline.
        if (!s.LockFilePresent || s.LockPid is null || !s.PidAlive)
            return SessionStatus.Offline;

        // Stale-detection has two thresholds depending on whether we have
        // reason to believe the agent is doing real work:
        //
        //   1. Working threshold (default 24h): even when the agent appears
        //      busy (turn or tool in flight), if events.jsonl hasn't been
        //      written for this long, the session has crashed mid-tool. The
        //      orphaned in-flight state is just leftover residue — treat
        //      as Offline.
        //   2. Idle threshold (default 4h): when the agent is NOT in any
        //      in-flight state, we can be more aggressive. No event in 4h
        //      with no work in progress = the session was abandoned.
        //
        // Falls back to UpdatedAt / CreatedAt when LastEventAt is null so
        // a session whose CLI never wrote an event still gets classified.
        bool agentBusy = s.InFlightTurn || s.InFlightTools.Count > 0;
        var lastSignal = s.LastEventAt ?? s.UpdatedAt ?? s.CreatedAt;

        if (lastSignal is { } last)
        {
            var age = now - last;
            if (age > EffectiveWorkingStaleThreshold) return SessionStatus.Offline;
            if (!agentBusy && age > StaleThreshold) return SessionStatus.Offline;
        }

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
