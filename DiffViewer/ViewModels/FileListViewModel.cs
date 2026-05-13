using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs <c>FileListView</c>. Holds the section structure, the active
/// display mode, and the currently-selected row. Population is push-based:
/// callers invoke <see cref="LoadFromChanges"/> whenever the underlying
/// <see cref="IRepositoryService"/> emits a change-list update.
/// </summary>
public sealed partial class FileListViewModel : ObservableObject
{
    public ObservableCollection<FileListSectionViewModel> Sections { get; } = new();

    /// <summary>
    /// Flat list shortcut used by the file-stepping keyboard shortcuts
    /// (Shift+F7/F8) so navigation works regardless of display mode.
    /// </summary>
    public ObservableCollection<FileEntryViewModel> FlatEntries { get; } = new();

    [ObservableProperty]
    private FileListDisplayMode _displayMode = FileListDisplayMode.RepoRelative;

    [ObservableProperty]
    private FileEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isFlatLayout;

    /// <summary>True when <see cref="DisplayMode"/> is the grouped tree view.</summary>
    public bool IsGroupedMode => DisplayMode == FileListDisplayMode.GroupedByDirectory;

    /// <summary>True when <see cref="DisplayMode"/> is one of the flat list modes.</summary>
    public bool IsFlatMode => !IsGroupedMode;

    public bool IsFullPathMode
    {
        get => DisplayMode == FileListDisplayMode.FullPath;
        set { if (value) DisplayMode = FileListDisplayMode.FullPath; }
    }

    public bool IsRepoRelativeMode
    {
        get => DisplayMode == FileListDisplayMode.RepoRelative;
        set { if (value) DisplayMode = FileListDisplayMode.RepoRelative; }
    }

    public bool IsGroupedByDirectoryMode
    {
        get => DisplayMode == FileListDisplayMode.GroupedByDirectory;
        set { if (value) DisplayMode = FileListDisplayMode.GroupedByDirectory; }
    }

    partial void OnDisplayModeChanged(FileListDisplayMode value)
    {
        foreach (var entry in FlatEntries) entry.ApplyDisplayMode(value);
        OnPropertyChanged(nameof(IsGroupedMode));
        OnPropertyChanged(nameof(IsFlatMode));
        OnPropertyChanged(nameof(IsFullPathMode));
        OnPropertyChanged(nameof(IsRepoRelativeMode));
        OnPropertyChanged(nameof(IsGroupedByDirectoryMode));
    }

    /// <summary>
    /// Replace all sections / entries with the supplied change list. Called
    /// from the UI thread.
    /// </summary>
    public void LoadFromChanges(IReadOnlyList<FileChange> changes, string repoRoot, bool isCommitVsCommit)
    {
        ArgumentNullException.ThrowIfNull(changes);

        Sections.Clear();
        FlatEntries.Clear();

        var entries = changes.Select(c =>
        {
            var e = new FileEntryViewModel(c, repoRoot);
            e.ApplyDisplayMode(DisplayMode);
            return e;
        }).ToList();
        foreach (var e in entries) FlatEntries.Add(e);

        if (isCommitVsCommit)
        {
            // No section grouping for commit-vs-commit - flat list under one synthetic section.
            IsFlatLayout = true;
            Sections.Add(new FileListSectionViewModel(WorkingTreeLayer.None, "Changes", entries));
            return;
        }

        IsFlatLayout = false;

        // Order: Conflicted, CommittedSinceCommit, Staged, Unstaged, Untracked.
        AddIfNonEmpty(WorkingTreeLayer.Conflicted, "Conflicted", entries);
        AddIfNonEmpty(WorkingTreeLayer.CommittedSinceCommit, "Committed since baseline", entries);
        AddIfNonEmpty(WorkingTreeLayer.Staged, "Staged", entries);
        AddIfNonEmpty(WorkingTreeLayer.Unstaged, "Unstaged", entries);
        AddIfNonEmpty(WorkingTreeLayer.Untracked, "Untracked", entries);
    }

    private void AddIfNonEmpty(WorkingTreeLayer layer, string header, IEnumerable<FileEntryViewModel> all)
    {
        var subset = all.Where(e => e.Change.Layer == layer)
                        .OrderBy(e => e.RepoRelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        if (subset.Count == 0) return;
        Sections.Add(new FileListSectionViewModel(layer, header, subset));
    }
}
