using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ISettingsService? _settingsService;
    private readonly IGitWriteService? _gitWriteService;
    private readonly DiffSide _left;
    private readonly DiffSide _right;
    private readonly bool _isCommitVsCommit;

    /// <summary>
    /// Stack of suspend tokens, one per in-flight git write operation. The
    /// outermost After-handler pops and disposes its token; nested ops
    /// keep the watcher suspended until they all complete.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentStack<IDisposable> _suspendTokens = new();

    /// <summary>
    /// Per-write snapshot of <c>.git\index</c> and <c>.git\HEAD</c> stats
    /// captured in BeforeOperation. After the op completes we compare to
    /// the now-current stats and force one extra refresh if either file
    /// changed beyond what our own op explains - covers the rare case of
    /// an external <c>git add</c> racing our own <c>git apply --cached</c>.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, RepoFileStatsSnapshot> _writeSnapshots = new();

    /// <summary>
    /// True iff a <see cref="RepositoryChangeKind"/> was observed while
    /// <see cref="DiffPaneViewModel.LiveUpdates"/> was off; we use it to
    /// fire one immediate refresh when Live is re-enabled (per the plan's
    /// pause-behaviour spec).
    /// </summary>
    private RepositoryChangeKind _missedChangeKindWhilePaused;

    public FileListViewModel FileList { get; }
    public DiffPaneViewModel DiffPane { get; }

    /// <summary>
    /// Hook for the View layer to display the modal Settings dialog.
    /// Tests / headless contexts leave it null and the
    /// <see cref="ShowSettingsCommand"/> becomes a no-op.
    /// </summary>
    public Action? ShowSettingsHandler { get; set; }

    [ObservableProperty]
    private string _windowTitle = "DiffViewer";

    [RelayCommand]
    private void ShowSettings() => ShowSettingsHandler?.Invoke();

    /// <summary>
    /// Settings service the View can use to construct a
    /// <see cref="SettingsViewModel"/> for the Settings dialog.
    /// </summary>
    public ISettingsService? SettingsService => _settingsService;

    public MainViewModel(
        IRepositoryService repository,
        DiffSide left,
        DiffSide right,
        IDiffService? diffService = null,
        IRepositoryWatcher? watcher = null,
        IPreDiffPass? preDiffPass = null,
        ISettingsService? settingsService = null,
        IGitWriteService? gitWriteService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _isCommitVsCommit = left is DiffSide.CommitIsh && right is DiffSide.CommitIsh;
        _watcher = watcher;
        _preDiffPass = preDiffPass;
        _settingsService = settingsService;
        _gitWriteService = gitWriteService;

        FileList = new FileListViewModel(settingsService);
        DiffPane = new DiffPaneViewModel(_repository, diffService, _isCommitVsCommit, settingsService);

        WindowTitle = $"DiffViewer — {repository.Shape.RepoRoot} ({left} ⇢ {right})";

        _repository.ChangeListUpdated += OnChangeListUpdated;
        FileList.PropertyChanged += OnFileListPropertyChanged;
        DiffPane.PropertyChanged += OnDiffPanePropertyChanged;

        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
            _watcher.Start();
        }

        if (_gitWriteService is not null)
        {
            _gitWriteService.BeforeOperation += OnBeforeWriteOperation;
            _gitWriteService.AfterOperation += OnAfterWriteOperation;
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

    private void OnBeforeWriteOperation(object? sender, GitWriteOperationEventArgs e)
    {
        // Snapshot index/HEAD stats so AfterOperation can detect external
        // races (someone ran `git add` in a terminal between our Before
        // and After events).
        _writeSnapshots[e.OperationId] = RepoFileStatsSnapshot.Capture(_repository.Shape.GitDir);

        // Suspend the watcher so its debounced refresh doesn't fire on
        // events generated by our own `git.exe` invocation.
        if (_watcher is not null)
        {
            _suspendTokens.Push(_watcher.Suspend());
        }
    }

    private void OnAfterWriteOperation(object? sender, GitWriteOperationEventArgs e)
    {
        try
        {
            // Per the plan: RefreshIndex first (drops LibGit2Sharp's stale
            // cache), then re-enumerate, then restart pre-diff, THEN resume
            // the watcher.
            MarshalToUi(() =>
            {
                RefreshChangeList();

                // Compare snapshots: if .git\index or .git\HEAD changed
                // beyond what our op explains, fire one more refresh to
                // pick up the external change (race window between
                // BeforeOperation and the actual git.exe completion).
                if (_writeSnapshots.TryRemove(e.OperationId, out var before))
                {
                    var after = RepoFileStatsSnapshot.Capture(_repository.Shape.GitDir);
                    if (after.IsExternallyChangedFrom(before, e.Result?.Success == true))
                    {
                        RefreshChangeList();
                    }
                }
            });
        }
        finally
        {
            if (_suspendTokens.TryPop(out var token))
            {
                token.Dispose();
            }
        }
    }

    private readonly struct RepoFileStatsSnapshot
    {
        public DateTime IndexMtime { get; }
        public long IndexSize { get; }
        public DateTime HeadMtime { get; }
        public long HeadSize { get; }

        private RepoFileStatsSnapshot(DateTime im, long isz, DateTime hm, long hs)
        { IndexMtime = im; IndexSize = isz; HeadMtime = hm; HeadSize = hs; }

        public static RepoFileStatsSnapshot Capture(string gitDir)
        {
            var index = new System.IO.FileInfo(System.IO.Path.Combine(gitDir, "index"));
            var head = new System.IO.FileInfo(System.IO.Path.Combine(gitDir, "HEAD"));
            return new RepoFileStatsSnapshot(
                index.Exists ? index.LastWriteTimeUtc : DateTime.MinValue,
                index.Exists ? index.Length : -1,
                head.Exists ? head.LastWriteTimeUtc : DateTime.MinValue,
                head.Exists ? head.Length : -1);
        }

        public bool IsExternallyChangedFrom(RepoFileStatsSnapshot before, bool ourOpSucceeded)
        {
            // Our own successful op is *expected* to bump index mtime/size.
            // We can't distinguish "our op caused this" from "external op
            // caused this" with just stats - but if the op FAILED and
            // either file changed, that's pure external race.
            if (!ourOpSucceeded)
            {
                return IndexMtime != before.IndexMtime || IndexSize != before.IndexSize
                    || HeadMtime != before.HeadMtime || HeadSize != before.HeadSize;
            }
            // For successful ops, only HEAD changes are unambiguously
            // external (we don't touch HEAD - git apply --cached etc.
            // mutate index only).
            return HeadMtime != before.HeadMtime || HeadSize != before.HeadSize;
        }
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
        if (_gitWriteService is not null)
        {
            _gitWriteService.BeforeOperation -= OnBeforeWriteOperation;
            _gitWriteService.AfterOperation -= OnAfterWriteOperation;
        }
        // Drop any leftover suspend tokens (paranoia - shouldn't happen
        // unless an op is in flight at shutdown).
        while (_suspendTokens.TryPop(out var token))
        {
            token.Dispose();
        }
        _preDiffPass?.Dispose();
        DiffPane.Dispose();
        _repository.Dispose();
    }
}
