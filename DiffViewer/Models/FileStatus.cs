namespace DiffViewer.Models;

/// <summary>
/// Git porcelain status code for a single change. Matches the two-letter
/// codes that <c>git status --porcelain</c> produces.
/// </summary>
public enum FileStatus
{
    Added,
    Deleted,
    Modified,
    Renamed,
    Copied,
    TypeChanged,
    Untracked,
    /// <summary>Two-letter conflict states (UU, AA, DU, UD, DD, AU, UA).</summary>
    Conflicted,
    /// <summary>Submodule whose recorded SHA changed.</summary>
    SubmoduleMoved,
}
