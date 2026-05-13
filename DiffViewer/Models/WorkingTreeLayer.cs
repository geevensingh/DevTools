namespace DiffViewer.Models;

/// <summary>
/// Which working-tree layer (relative to HEAD or another commit) a
/// <see cref="FileChange"/> belongs to. Drives the section grouping in the
/// left pane. Conflicted entries get their own layer because they bypass
/// the standard two-pane diff in favour of the 3-way placeholder.
/// </summary>
public enum WorkingTreeLayer
{
    /// <summary>Commit-vs-commit comparison (no working-tree layering).</summary>
    None,

    /// <summary>Conflicted entries from <c>repo.Index.Conflicts</c>.</summary>
    Conflicted,

    /// <summary>Differences between <c>HEAD</c> and the user-supplied commit.</summary>
    CommittedSinceCommit,

    /// <summary>Differences between <c>HEAD</c> and the index.</summary>
    Staged,

    /// <summary>Differences between the index and the working-tree file.</summary>
    Unstaged,

    /// <summary>Working-tree files not tracked by the repo.</summary>
    Untracked,
}
