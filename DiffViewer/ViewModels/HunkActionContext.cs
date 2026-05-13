using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Parameter for diff-pane hunk write commands. Carries the side that was
/// right-clicked and the 1-based caret line within that pane; the command
/// resolves the hunk lazily via <see cref="DiffPaneViewModel.HunkAtLine"/>
/// so we never serialise a stale hunk reference across the menu-open ⟶
/// click window.
/// </summary>
public sealed record HunkActionContext(ChangeSide Side, int OneBasedLine);

/// <summary>
/// Parameter for "open in editor at this line". Carries the file entry and
/// the 1-based caret line in whichever pane the right-click happened.
/// </summary>
public sealed record LineActionContext(FileEntryViewModel? Entry, int OneBasedLine);
