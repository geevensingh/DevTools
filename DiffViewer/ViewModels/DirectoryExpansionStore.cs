namespace DiffViewer.ViewModels;

/// <summary>
/// Tracks which directory nodes are <em>collapsed</em> across rebuilds of
/// the file list. Defaults to "everything expanded" — only directories the
/// user has explicitly collapsed are remembered, by stable full-path key.
///
/// <para>Lives on <see cref="FileListViewModel"/> for the lifetime of the
/// app run; not persisted to disk in v1 (the user only complained about
/// state being lost when adding/removing files mid-session, not across
/// app restarts).</para>
/// </summary>
public sealed class DirectoryExpansionStore
{
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);

    public bool IsCollapsed(string key) =>
        !string.IsNullOrEmpty(key) && _collapsed.Contains(key);

    public void Set(string key, bool isCollapsed)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (isCollapsed) _collapsed.Add(key);
        else _collapsed.Remove(key);
    }
}
