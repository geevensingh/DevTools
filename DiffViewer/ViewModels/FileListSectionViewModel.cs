using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// One section of the left-pane file list: Conflicted, CommittedSinceCommit,
/// Staged, Unstaged, or Untracked. Sections are absent for commit-vs-commit
/// comparisons (the list is flat).
/// </summary>
public sealed partial class FileListSectionViewModel : ObservableObject
{
    public WorkingTreeLayer Layer { get; }
    public string Header { get; }
    public ObservableCollection<FileEntryViewModel> Entries { get; }
    public ObservableCollection<DirectoryNodeViewModel> RootDirectories { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public FileListSectionViewModel(WorkingTreeLayer layer, string header, IEnumerable<FileEntryViewModel> entries)
        : this(layer, header, entries, store: null) { }

    public FileListSectionViewModel(
        WorkingTreeLayer layer, string header,
        IEnumerable<FileEntryViewModel> entries,
        DirectoryExpansionStore? store)
    {
        Layer = layer;
        Header = header;
        Entries = new ObservableCollection<FileEntryViewModel>(entries);
        RootDirectories = new ObservableCollection<DirectoryNodeViewModel>(
            DirectoryNodeViewModel.Build(Entries, sectionKey: layer.ToString(), store: store));
    }
}
