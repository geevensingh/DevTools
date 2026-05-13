using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Read-only access to a git repository: enumerate the change list between
/// two <see cref="DiffSide"/>s, fetch blob bytes through the clean/smudge
/// filter chain, watch for repo loss / index changes. Write operations
/// live in <see cref="IGitWriteService"/>.
/// </summary>
public interface IRepositoryService : IDisposable
{
    /// <summary>Static facts about the repo captured at open time.</summary>
    RepositoryShape Shape { get; }

    /// <summary>The most recently enumerated change list. Empty until <see cref="EnumerateChanges"/> succeeds.</summary>
    IReadOnlyList<FileChange> CurrentChanges { get; }

    /// <summary>Raised whenever the change list is recomputed.</summary>
    event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated;

    /// <summary>Raised when the repo on disk becomes unreadable or vanishes.</summary>
    event EventHandler<RepositoryLostEventArgs>? RepositoryLost;

    /// <summary>Resolve a commit-ish reference; returns <c>null</c> if it doesn't resolve.</summary>
    string? ResolveCommitIsh(string reference);

    /// <summary>Both refs resolve to commits and are reachable.</summary>
    bool ValidateRevisions(string leftRef, string rightRef);

    /// <summary>
    /// Enumerate the full change list between <paramref name="left"/> and
    /// <paramref name="right"/>. Updates <see cref="CurrentChanges"/> and
    /// raises <see cref="ChangeListUpdated"/> on success.
    /// </summary>
    IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right);

    /// <summary>
    /// Read the contents of one side of a single file change. Applies the
    /// clean/smudge filter chain and detects encoding / binary / LFS-pointer
    /// state.
    /// </summary>
    BlobContent ReadSide(FileChange change, ChangeSide side);

    /// <summary>
    /// Drop LibGit2Sharp's in-memory index cache and re-read <c>.git\index</c>
    /// from disk. Required after every external <c>git.exe</c> mutation.
    /// </summary>
    void RefreshIndex();

    /// <summary>
    /// Re-resolve the current state of <paramref name="path"/> as a
    /// <see cref="FileChange"/> in the supplied layer. Returns <c>null</c>
    /// if the path no longer differs in that layer (used by write-op
    /// preflight to close the menu-open ⟶ click race).
    /// </summary>
    FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer);

    /// <summary>Reopen the repo after <see cref="RepositoryLost"/>; returns true on success.</summary>
    bool TryReopen();

    /// <summary>Atomic snapshot of the current change list under the same lock that wires up the subscription.</summary>
    (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
        EventHandler<ChangeListUpdatedEventArgs> handler);
}

/// <summary>Which side of a <see cref="FileChange"/> to read.</summary>
public enum ChangeSide
{
    Left,
    Right,
}
