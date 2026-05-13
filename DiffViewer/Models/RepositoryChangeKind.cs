namespace DiffViewer.Models;

/// <summary>
/// Categorises a watcher-fired repo change so consumers know which slice
/// of state to refresh. Bitmask-friendly so multiple kinds can collapse
/// into one debounced fire.
/// </summary>
[Flags]
public enum RepositoryChangeKind
{
    None = 0,

    /// <summary>A file under the working directory created/modified/renamed/deleted.</summary>
    WorkingTree = 1 << 0,

    /// <summary><c>.git\HEAD</c>, <c>.git\index</c>, or another tracked
    /// state file (MERGE_HEAD, REBASE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD)
    /// changed - external git command ran.</summary>
    GitDir = 1 << 1,

    /// <summary>The OS FSW kernel buffer overflowed; consumers should do a
    /// full re-enumeration. Watcher infrastructure has already been
    /// reconstructed by the time this fires.</summary>
    BufferOverflow = 1 << 2,
}
