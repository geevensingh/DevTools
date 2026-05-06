namespace CopilotSessionMonitor.Core;

/// <summary>
/// Pluggable session source. <see cref="LocalCliSessionSource"/> is the only
/// implementation today. The <c>RemoteCodingAgentSource</c> hook described in
/// the plan would slot in as a second implementation feeding the same
/// aggregator.
/// </summary>
public interface ISessionSource : IDisposable
{
    /// <summary>Snapshot of all known sessions at this instant.</summary>
    IReadOnlyCollection<SessionState> Snapshot();

    /// <summary>Raised when the set of sessions or any individual session changes.</summary>
    event EventHandler? SessionsChanged;

    /// <summary>Re-poll all sources (lock files, git status, etc.).</summary>
    void Refresh();
}
