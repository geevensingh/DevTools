namespace DiffViewer.Models;

/// <summary>
/// Payload for <see cref="Services.IRepositoryWatcher.Changed"/>. Carries
/// the bitmask of change kinds that accumulated during the last debounce
/// window, plus a timestamp for diagnostics.
/// </summary>
public sealed class RepositoryChangedEventArgs : EventArgs
{
    public RepositoryChangeKind Kind { get; }
    public DateTime DetectedAt { get; }

    public RepositoryChangedEventArgs(RepositoryChangeKind kind, DateTime detectedAt)
    {
        Kind = kind;
        DetectedAt = detectedAt;
    }
}
