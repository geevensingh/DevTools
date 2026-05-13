using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiffViewer.ViewModels;

/// <summary>
/// A single directory node in the grouped-by-directory tree view. Contains
/// child directory nodes plus leaf file entries. Empty intermediate directories
/// collapse into the parent's display label (e.g. <c>src\bar\</c> rather than
/// <c>src\</c> &gt; <c>bar\</c>).
/// </summary>
public sealed partial class DirectoryNodeViewModel : ObservableObject
{
    private readonly DirectoryExpansionStore? _expansionStore;

    public string Label { get; }

    /// <summary>
    /// Stable identity used by the <see cref="DirectoryExpansionStore"/> to
    /// remember collapse state across rebuilds. Built by joining the
    /// section header to the full directory path with single-child chains
    /// re-expanded so an intermediate rename doesn't lose state.
    /// </summary>
    public string PersistenceKey { get; }

    public ObservableCollection<DirectoryNodeViewModel> Children { get; } = new();
    public ObservableCollection<FileEntryViewModel> Files { get; } = new();

    /// <summary>
    /// Combined view used by <c>TreeView</c>'s <c>HierarchicalDataTemplate</c>
    /// for the grouped-by-directory mode. Sub-directories first, then files.
    /// WPF dispatches each item to the matching <c>DataTemplate</c> by type.
    /// </summary>
    public IEnumerable<object> ChildrenAndFiles =>
        Children.Cast<object>().Concat(Files.Cast<object>());

    [ObservableProperty]
    private bool _isExpanded = true;

    partial void OnIsExpandedChanged(bool value)
    {
        _expansionStore?.Set(PersistenceKey, isCollapsed: !value);
    }

    public DirectoryNodeViewModel(string label)
        : this(label, persistenceKey: string.Empty, store: null) { }

    public DirectoryNodeViewModel(string label, string persistenceKey, DirectoryExpansionStore? store)
    {
        Label = label;
        PersistenceKey = persistenceKey;
        _expansionStore = store;
        if (store is not null && store.IsCollapsed(persistenceKey))
        {
            _isExpanded = false;
        }
    }

    /// <summary>
    /// Build the directory tree from a flat list of file entries. Collapses
    /// chains of single-child directories into one node (e.g. <c>a\b\c</c>
    /// stays as one label rather than three nested nodes).
    /// </summary>
    public static IEnumerable<DirectoryNodeViewModel> Build(IEnumerable<FileEntryViewModel> entries) =>
        Build(entries, sectionKey: string.Empty, store: null);

    /// <summary>
    /// Build the directory tree from a flat list of file entries, hooking
    /// each node up to the provided <paramref name="store"/> so collapse
    /// state survives rebuilds (e.g. after a file is added or removed).
    /// </summary>
    public static IEnumerable<DirectoryNodeViewModel> Build(
        IEnumerable<FileEntryViewModel> entries,
        string sectionKey,
        DirectoryExpansionStore? store)
    {
        var roots = new Dictionary<string, DirectoryNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var rootFiles = new List<FileEntryViewModel>();

        foreach (var entry in entries)
        {
            var directory = entry.DirectoryPath;
            if (string.IsNullOrEmpty(directory))
            {
                rootFiles.Add(entry);
                continue;
            }

            var segments = directory.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            var current = GetOrAddRoot(roots, segments[0], sectionKey, store);
            for (int i = 1; i < segments.Length; i++)
            {
                current = GetOrAddChild(current, segments[i], sectionKey, store);
            }
            current.Files.Add(entry);
        }

        // Sort directories (case-insensitive), then files within directories.
        foreach (var root in roots.Values)
        {
            SortRecursive(root);
        }

        var result = new List<DirectoryNodeViewModel>();
        foreach (var root in roots.Values.OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(CollapseSingleChildChains(root, sectionKey, store));
        }

        // Files at repo root sit under a synthetic node so the view stays uniform.
        if (rootFiles.Count > 0)
        {
            var rootKey = MakeKey(sectionKey, "<root>");
            var rootNode = new DirectoryNodeViewModel(string.Empty, rootKey, store);
            foreach (var f in rootFiles.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase))
            {
                rootNode.Files.Add(f);
            }
            result.Insert(0, rootNode);
        }

        return result;
    }

    private static string MakeKey(string sectionKey, string fullDirPath) =>
        $"{sectionKey}::{fullDirPath}";

    private static DirectoryNodeViewModel GetOrAddRoot(
        Dictionary<string, DirectoryNodeViewModel> roots, string label,
        string sectionKey, DirectoryExpansionStore? store)
    {
        if (!roots.TryGetValue(label, out var node))
        {
            node = new DirectoryNodeViewModel(label, MakeKey(sectionKey, label), store);
            roots[label] = node;
        }
        return node;
    }

    private static DirectoryNodeViewModel GetOrAddChild(
        DirectoryNodeViewModel parent, string label,
        string sectionKey, DirectoryExpansionStore? store)
    {
        var existing = parent.Children.FirstOrDefault(c =>
            string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        // Derive the child's full directory path from the parent's persistence
        // key so siblings share a stable prefix (parent's section-prefixed path).
        var parentDirPath = StripSectionPrefix(parent.PersistenceKey, sectionKey);
        var childDirPath = string.IsNullOrEmpty(parentDirPath)
            ? label
            : parentDirPath + Path.DirectorySeparatorChar + label;

        var child = new DirectoryNodeViewModel(label, MakeKey(sectionKey, childDirPath), store);
        parent.Children.Add(child);
        return child;
    }

    private static string StripSectionPrefix(string persistenceKey, string sectionKey)
    {
        var prefix = $"{sectionKey}::";
        return persistenceKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? persistenceKey[prefix.Length..]
            : persistenceKey;
    }

    private static void SortRecursive(DirectoryNodeViewModel node)
    {
        var sortedDirs = node.Children.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();
        node.Children.Clear();
        foreach (var d in sortedDirs)
        {
            SortRecursive(d);
            node.Children.Add(d);
        }

        var sortedFiles = node.Files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        node.Files.Clear();
        foreach (var f in sortedFiles) node.Files.Add(f);
    }

    /// <summary>
    /// Collapse <c>a → b → c</c> single-child chains into a single node
    /// labelled <c>a\b\c</c>. The collapse is applied recursively, so a
    /// chain anywhere in the tree (not just at the root) gets folded.
    /// </summary>
    private static DirectoryNodeViewModel CollapseSingleChildChains(
        DirectoryNodeViewModel node, string sectionKey, DirectoryExpansionStore? store)
    {
        var current = node;
        var label = node.Label;
        var dirPath = StripSectionPrefix(node.PersistenceKey, sectionKey);
        while (current.Files.Count == 0 && current.Children.Count == 1)
        {
            var only = current.Children[0];
            label = label + Path.DirectorySeparatorChar + only.Label;
            dirPath = StripSectionPrefix(only.PersistenceKey, sectionKey);
            current = only;
        }

        var collapsed = new DirectoryNodeViewModel(label, MakeKey(sectionKey, dirPath), store);
        foreach (var f in current.Files) collapsed.Files.Add(f);
        foreach (var c in current.Children) collapsed.Children.Add(CollapseSingleChildChains(c, sectionKey, store));
        return collapsed;
    }
}
