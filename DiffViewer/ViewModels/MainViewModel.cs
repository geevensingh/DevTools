using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Root view-model. Owns the repository service, the optional repository
/// watcher, the file list, and the right-pane state. Constructed by
/// <c>CompositionRoot</c> once command-line parsing succeeds.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IRepositoryService _repository;
    private readonly IRepositoryWatcher? _watcher;
    private readonly IPreDiffPass? _preDiffPass;
    private readonly DiffSide _left;
    private readonly DiffSide _right;
    private readonly bool _isCommitVsCommit;

    /// <summary>
    /// True iff a <see cref="RepositoryChangeKind"/> was observed while
    /// <see cref="DiffPaneViewModel.LiveUpdates"/> was off; we use it to
    /// fire one immediate refresh when Live is re-enabled (per the plan's
    /// pause-behaviour spec).
    /// </summary>
    private RepositoryChangeKind _missedChangeKindWhilePaused;

    public FileListViewModel FileList { get; } = new();
    public DiffPaneViewModel DiffPane { get; }

    [ObservableProperty]
    private string _windowTitle = "DiffViewer";

    public MainViewModel(
        IRepositoryService repository,
        DiffSide left,
        DiffSide right,
        IDiffService? diffService = null,
        IRepositoryWatcher? watcher = null,
        IPreDiffPass? preDiffPass = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _isCommitVsCommit = left is DiffSide.CommitIsh && right is DiffSide.CommitIsh;
        _watcher = watcher;
        _preDiffPass = preDiffPass;

        DiffPane = new DiffPaneViewModel(_repository, diffService, _isCommitVsCommit);

        WindowTitle = $"DiffViewer — {repository.Shape.RepoRoot} ({left} ⇢ {right})";

        _repository.ChangeListUpdated += OnChangeListUpdated;
        FileList.PropertyChanged += OnFileListPropertyChanged;
        DiffPane.PropertyChanged += OnDiffPanePropertyChanged;

        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
            _watcher.Start();
        }
    }

    /// <summary>Trigger the initial change-list load (called once at startup).</summary>
    public void LoadInitialChanges()
    {
        var changes = _repository.EnumerateChanges(_left, _right);
        FileList.LoadFromChanges(changes, _repository.Shape.RepoRoot, _isCommitVsCommit);
        StartPreDiffPass();
    }

    /// <summary>
    /// Refresh the change list (re-enumerate). Public so F5 / write-op
    /// completion can trigger it directly. Must run on the UI thread when
    /// it touches FileList.
    /// </summary>
    public void RefreshChangeList()
    {
        _repository.RefreshIndex();
        var changes = _repository.EnumerateChanges(_left, _right);
        FileList.LoadFromChanges(changes, _repository.Shape.RepoRoot, _isCommitVsCommit);
        StartPreDiffPass();
    }

    private void StartPreDiffPass()
    {
        _preDiffPass?.Start(
            FileList.FlatEntries.ToList(),
            FileList.SelectedEntry,
            DiffPane.BuildDiffOptions());
    }

    private void OnFileListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileListViewModel.SelectedEntry)) return;
        // Fire-and-forget: the VM serialises in-flight loads via its own CTS.
        _ = DiffPane.LoadAsync(FileList.SelectedEntry);
        // Reprioritise the pre-diff pass so the new selection jumps the queue.
        _preDiffPass?.OnSelectionChanged(FileList.SelectedEntry);
    }

    private void OnDiffPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When the user re-enables Live updates after we observed events
        // while paused, fire one immediate refresh - per the plan's
        // pause-behaviour rule.
        if (e.PropertyName == nameof(DiffPaneViewModel.LiveUpdates) &&
            DiffPane.LiveUpdatesEffective &&
            _missedChangeKindWhilePaused != RepositoryChangeKind.None)
        {
            _missedChangeKindWhilePaused = RepositoryChangeKind.None;
            MarshalToUi(RefreshChangeList);
            return;
        }

        // Toolbar option toggle that changes the diff result - re-stamp
        // every entry's HasVisibleDifferences. Today only IgnoreWhitespace
        // affects the result; the others are display-only.
        if (e.PropertyName == nameof(DiffPaneViewModel.IgnoreWhitespace))
        {
            _preDiffPass?.OnOptionsChanged(
                FileList.FlatEntries.ToList(),
                FileList.SelectedEntry,
                DiffPane.BuildDiffOptions());
        }
    }

    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e)
    {
        // If the user has Live updates off (intentionally OR because we're
        // commit-vs-commit), buffer the event and refresh on resume.
        if (!DiffPane.LiveUpdatesEffective)
        {
            _missedChangeKindWhilePaused |= e.Kind;
            return;
        }

        MarshalToUi(RefreshChangeList);
    }

    private void OnChangeListUpdated(object? sender, ChangeListUpdatedEventArgs e)
    {
        MarshalToUi(() =>
        {
            FileList.LoadFromChanges(e.Changes, _repository.Shape.RepoRoot, _isCommitVsCommit);
            StartPreDiffPass();
        });
    }

    private static void MarshalToUi(Action action)
    {
        if (System.Windows.Application.Current is { Dispatcher: { } d } && !d.CheckAccess())
        {
            d.BeginInvoke(action);
            return;
        }
        action();
    }

    public void Dispose()
    {
        FileList.PropertyChanged -= OnFileListPropertyChanged;
        DiffPane.PropertyChanged -= OnDiffPanePropertyChanged;
        _repository.ChangeListUpdated -= OnChangeListUpdated;
        if (_watcher is not null)
        {
            _watcher.Changed -= OnRepositoryChanged;
            _watcher.Dispose();
        }
        _preDiffPass?.Dispose();
        DiffPane.Dispose();
        _repository.Dispose();
    }
}
