namespace DiffViewer.Models;

/// <summary>
/// Why <see cref="DiffViewer.Services.IRepositoryService.RepositoryLost"/>
/// fired. Drives the banner copy in the lost-repo overlay.
/// </summary>
public enum RepositoryLossReason
{
    DotGitMissing,
    RepoRootMissing,
    AccessDenied,
    Other,
}

/// <summary>
/// Raised on <see cref="DiffViewer.Services.IRepositoryService.RepositoryLost"/>.
/// </summary>
public sealed class RepositoryLostEventArgs : EventArgs
{
    public required string RepoRoot { get; init; }
    public required RepositoryLossReason Reason { get; init; }
    public string? ExceptionMessage { get; init; }
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Raised on <see cref="DiffViewer.Services.IRepositoryService.ChangeListUpdated"/>
/// — the change list has been re-enumerated.
/// </summary>
public sealed class ChangeListUpdatedEventArgs : EventArgs
{
    public required IReadOnlyList<FileChange> Changes { get; init; }
}
