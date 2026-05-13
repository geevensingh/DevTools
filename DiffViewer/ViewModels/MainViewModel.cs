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
    private readonly IExternalAppLauncher? _externalAppLauncher;
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

    /// <summary>
    /// Hook for the View layer to display a confirmation dialog. Returns
    /// <c>true</c> if the user proceeds, <c>false</c> on cancel. Tests /
    /// headless contexts leave it null and destructive commands are
    /// suppressed entirely (fail-safe).
    /// </summary>
    public Func<ConfirmationRequest, ConfirmationResult>? ConfirmHandler { get; set; }

    /// <summary>
    /// Hook to display a transient toast / status message. Tests can leave
    /// it null. Used for "added to .gitignore", error surfaces from
    /// git.exe, "couldn't open settings.json", etc.
    /// </summary>
    public Action<string>? ToastHandler { get; set; }

    // ---------------- File-list context-menu commands ----------------

    [RelayCommand]
    private void ShowInExplorer(FileEntryViewModel? entry)
    {
        if (entry is null || _externalAppLauncher is null) return;
        var result = _externalAppLauncher.ShowInExplorer(entry.FullPath);
        if (!result.Success) ToastHandler?.Invoke(result.ErrorMessage ?? "Show-in-Explorer failed.");
    }

    [RelayCommand]
    private void OpenWithDefaultApp(FileEntryViewModel? entry)
    {
        if (entry is null || _externalAppLauncher is null) return;
        var result = _externalAppLauncher.OpenWithDefaultApp(entry.FullPath);
        if (!result.Success) ToastHandler?.Invoke(result.ErrorMessage ?? "Open failed.");
    }

    [RelayCommand]
    private async Task OpenInExternalEditor(FileEntryViewModel? entry)
    {
        if (entry is null || _externalAppLauncher is null) return;
        var result = await _externalAppLauncher.LaunchEditorAsync(entry.FullPath, line: 0).ConfigureAwait(false);
        if (!result.Success) ToastHandler?.Invoke(result.ErrorMessage ?? "Editor launch failed.");
    }

    [RelayCommand]
    private async Task StageFile(FileEntryViewModel? entry)
    {
        if (entry is null || _gitWriteService is null) return;
        var r = await _gitWriteService.StageFileAsync(_repository.Shape.RepoRoot, entry.Change.Path).ConfigureAwait(false);
        if (!r.Success) ToastHandler?.Invoke($"Stage failed: {r.StdErr}");
    }

    [RelayCommand]
    private async Task DeleteFile(FileEntryViewModel? entry)
    {
        if (entry is null || _gitWriteService is null) return;

        var suppress = _settingsService?.Current.SuppressDeleteFileConfirmation ?? false;
        if (!suppress)
        {
            var resp = ConfirmHandler?.Invoke(new ConfirmationRequest(
                Title: "Delete file?",
                Message: $"Move '{entry.RepoRelativePath}' to the Recycle Bin?\n\nIt can be restored from there.",
                ConfirmText: "Delete",
                CancelText: "Cancel",
                ShowDontAskAgain: true));

            if (resp is null) return;
            if (!resp.Confirmed) return;
            if (resp.DontAskAgain && _settingsService is not null)
            {
                _settingsService.Update(s => s with { SuppressDeleteFileConfirmation = true });
            }
        }

        var r = await _gitWriteService.DeleteToRecycleBinAsync(_repository.Shape.RepoRoot, entry.FullPath).ConfigureAwait(false);
        if (!r.Success) ToastHandler?.Invoke($"Delete failed: {r.StdErr}");
    }

    [RelayCommand]
    private async Task AddToGitignore(FileEntryViewModel? entry)
    {
        if (entry is null || _gitWriteService is null) return;
        var r = await _gitWriteService.AddToGitignoreAsync(_repository.Shape.RepoRoot, entry.Change.Path).ConfigureAwait(false);
        ToastHandler?.Invoke(r.Success ? r.StdOut : $"Add to .gitignore failed: {r.StdErr}");
    }

    [RelayCommand]
    private void CopyFileName(FileEntryViewModel? entry) => CopyToClipboard(entry?.FileName);

    [RelayCommand]
    private void CopyRepoRelativePath(FileEntryViewModel? entry) => CopyToClipboard(entry?.RepoRelativePath);

    [RelayCommand]
    private void CopyFullPath(FileEntryViewModel? entry) => CopyToClipboard(entry?.FullPath);

    [RelayCommand]
    private void CopyLeftBlobSha(FileEntryViewModel? entry) => CopyToClipboard(entry?.Change.LeftBlobSha);

    [RelayCommand]
    private void CopyRightBlobSha(FileEntryViewModel? entry) => CopyToClipboard(entry?.Change.RightBlobSha);

    [RelayCommand]
    private void RefreshList() => RefreshChangeList();

    // ---------------- Diff-pane context-menu commands ----------------

    [RelayCommand]
    private async Task StageHunkAtCaret(HunkActionContext? ctx)
    {
        if (!TryGetHunkAction(ctx, out var entry, out var inputs)) return;
        var r = await _gitWriteService!.StageHunkAsync(_repository.Shape.RepoRoot, inputs).ConfigureAwait(false);
        if (!r.Success) ToastHandler?.Invoke($"Stage hunk failed: {r.StdErr}");
    }

    [RelayCommand]
    private async Task UnstageHunkAtCaret(HunkActionContext? ctx)
    {
        if (!TryGetHunkAction(ctx, out var entry, out var inputs)) return;
        var r = await _gitWriteService!.UnstageHunkAsync(_repository.Shape.RepoRoot, inputs).ConfigureAwait(false);
        if (!r.Success) ToastHandler?.Invoke($"Unstage hunk failed: {r.StdErr}");
    }

    [RelayCommand]
    private async Task RevertHunkAtCaret(HunkActionContext? ctx)
    {
        if (!TryGetHunkAction(ctx, out var entry, out var inputs)) return;

        var suppress = _settingsService?.Current.SuppressRevertHunkConfirmation ?? false;
        if (!suppress)
        {
            var preview = inputs.Hunk.Lines.Take(3).Select(l => l.Text).ToArray();
            var resp = ConfirmHandler?.Invoke(new ConfirmationRequest(
                Title: "Revert hunk?",
                Message: "Discard this hunk from the working tree? This cannot be undone via git.\n\n"
                       + "Preview:\n" + string.Join("\n", preview)
                       + (inputs.Hunk.Lines.Count > 3 ? "\n…" : ""),
                ConfirmText: "Revert",
                CancelText: "Cancel",
                ShowDontAskAgain: true));

            if (resp is null) return;
            if (!resp.Confirmed) return;
            if (resp.DontAskAgain && _settingsService is not null)
            {
                _settingsService.Update(s => s with { SuppressRevertHunkConfirmation = true });
            }
        }

        var r = await _gitWriteService!.RevertHunkAsync(_repository.Shape.RepoRoot, inputs).ConfigureAwait(false);
        if (!r.Success) ToastHandler?.Invoke($"Revert hunk failed: {r.StdErr}");
    }

    [RelayCommand]
    private async Task OpenInExternalEditorAtLine(LineActionContext? ctx)
    {
        if (ctx?.Entry is null || _externalAppLauncher is null) return;
        var r = await _externalAppLauncher.LaunchEditorAsync(ctx.Entry.FullPath, ctx.OneBasedLine).ConfigureAwait(false);
        if (!r.Success) ToastHandler?.Invoke(r.ErrorMessage ?? "Editor launch failed.");
    }

    // ---------------- Keyboard-shortcut commands ----------------
    // Cross-file / cross-section hunk navigation (F7/F8 family).
    // Toggle aliases for toolbar state (Ctrl+W, Ctrl+I, etc.).
    // Display-mode setters (Ctrl+1/2/3).
    // Manual refresh (F5).

    /// <summary>
    /// Hook for the View to perform F6 focus cycling. The focus mechanics
    /// (FocusManager.SetFocusedElement on file list / left editor / right
    /// editor) live in the View; the VM just requests the next stop.
    /// </summary>
    public Action? FocusCycleRequested { get; set; }

    /// <summary>F6 — cycle focus across the 3 stops (file list / left / right).</summary>
    [RelayCommand]
    private void CycleFocus() => FocusCycleRequested?.Invoke();

    [RelayCommand]
    private void Refresh()
    {
        if (_isCommitVsCommit && !_repository.ValidateRevisions(
                (_left as DiffSide.CommitIsh)?.Reference ?? "",
                (_right as DiffSide.CommitIsh)?.Reference ?? ""))
        {
            ToastHandler?.Invoke("Comparison source is no longer reachable.");
            return;
        }
        RefreshChangeList();
        ToastHandler?.Invoke(FileList.FlatEntries.Count == 0 ? "No changes." : "Refreshed.");
    }

    [RelayCommand] private void ToggleIgnoreWhitespace()  => DiffPane.IgnoreWhitespace  = !DiffPane.IgnoreWhitespace;
    [RelayCommand] private void ToggleVisibleWhitespace() => DiffPane.ShowVisibleWhitespace = !DiffPane.ShowVisibleWhitespace;
    [RelayCommand] private void ToggleSideBySide()        => DiffPane.IsSideBySide      = !DiffPane.IsSideBySide;
    [RelayCommand] private void ToggleIntraLineDiff()     => DiffPane.ShowIntraLineDiff = !DiffPane.ShowIntraLineDiff;

    [RelayCommand]
    private void ToggleLiveUpdates()
    {
        if (!DiffPane.IsLiveUpdatesAvailable) return;  // greyed out for commit-vs-commit
        DiffPane.LiveUpdates = !DiffPane.LiveUpdates;
    }

    [RelayCommand] private void SetDisplayModeFullPath()     => FileList.DisplayMode = FileListDisplayMode.FullPath;
    [RelayCommand] private void SetDisplayModeRepoRelative() => FileList.DisplayMode = FileListDisplayMode.RepoRelative;
    [RelayCommand] private void SetDisplayModeGrouped()      => FileList.DisplayMode = FileListDisplayMode.GroupedByDirectory;

    /// <summary>
    /// F8 — step to the next visible change. Within a file, advances to the
    /// next hunk; at the last hunk, advances to the first hunk of the next
    /// file. Crosses section boundaries naturally; cycles around at the
    /// last file in the last section. Whitespace-only files (no visible
    /// hunks) are skipped.
    /// </summary>
    [RelayCommand]
    private async Task NavigateNextChange()
    {
        if (DiffPane.TryNavigateNextHunkInFile()) return;
        await StepFile(forward: true, jumpToFirst: true).ConfigureAwait(true);
    }

    /// <summary>F7 — symmetric mirror of <see cref="NavigateNextChange"/>.</summary>
    [RelayCommand]
    private async Task NavigatePreviousChange()
    {
        if (DiffPane.TryNavigatePreviousHunkInFile()) return;
        await StepFile(forward: false, jumpToFirst: false).ConfigureAwait(true);
    }

    /// <summary>Shift+F8 — skip the per-hunk steps; cycle through files.</summary>
    [RelayCommand]
    private Task NavigateNextFile() => StepFile(forward: true, jumpToFirst: true);

    /// <summary>Shift+F7 — symmetric mirror of <see cref="NavigateNextFile"/>.</summary>
    [RelayCommand]
    private Task NavigatePreviousFile() => StepFile(forward: false, jumpToFirst: true);

    /// <summary>Ctrl+F8 — jump to the first file of the next section.</summary>
    [RelayCommand]
    private Task NavigateNextSection() => StepSection(forward: true);

    /// <summary>Ctrl+F7 — jump to the first file of the previous section.</summary>
    [RelayCommand]
    private Task NavigatePreviousSection() => StepSection(forward: false);

    /// <summary>
    /// Move the file-list selection by one in <see cref="FileList.FlatEntries"/>,
    /// skipping rows whose pre-diff pass marked them as having no visible
    /// differences (e.g., whitespace-only when *Ignore whitespace* is on).
    /// On no-change-anywhere, no-op. After selection change, awaits the
    /// resulting <see cref="DiffPaneViewModel.LastLoadTask"/> and seeks to
    /// the first/last hunk per <paramref name="jumpToFirst"/>.
    /// </summary>
    private async Task StepFile(bool forward, bool jumpToFirst)
    {
        var entries = FileList.FlatEntries;
        if (entries.Count == 0) return;

        int start = entries.IndexOf(FileList.SelectedEntry!);
        if (start < 0) start = forward ? -1 : entries.Count;  // pre-first / post-last

        var next = FindNextWithChanges(entries, start, forward);
        if (next is null) return;

        FileList.SelectedEntry = next;
        // The PropertyChanged handler kicks off DiffPane.LoadAsync; await
        // it before issuing the jump so CurrentHunks is populated.
        await DiffPane.LastLoadTask.ConfigureAwait(true);
        if (jumpToFirst) DiffPane.JumpToFirstHunk();
        else             DiffPane.JumpToLastHunk();
    }

    private async Task StepSection(bool forward)
    {
        var sections = FileList.Sections;
        if (sections.Count == 0) return;

        var currentEntry = FileList.SelectedEntry;
        int currentSec = currentEntry is null ? -1
            : IndexOfSectionContaining(sections, currentEntry);

        for (int hop = 1; hop <= sections.Count; hop++)
        {
            int idx = forward
                ? (currentSec + hop) % sections.Count
                : ((currentSec - hop) % sections.Count + sections.Count) % sections.Count;
            var sec = sections[idx];
            var first = sec.Entries.FirstOrDefault(e =>
                e.HasVisibleDifferences ?? true);  // unknown counts as eligible
            if (first is not null)
            {
                FileList.SelectedEntry = first;
                await DiffPane.LastLoadTask.ConfigureAwait(true);
                DiffPane.JumpToFirstHunk();
                return;
            }
        }
    }

    private static int IndexOfSectionContaining(
        IReadOnlyList<FileListSectionViewModel> sections,
        FileEntryViewModel entry)
    {
        for (int i = 0; i < sections.Count; i++)
            if (sections[i].Entries.Contains(entry)) return i;
        return -1;
    }

    /// <summary>
    /// Walk <paramref name="entries"/> from <paramref name="from"/> in
    /// <paramref name="forward"/> direction, returning the first row with
    /// visible differences (or unknown). Cycles around. Returns null when
    /// every row has been confirmed whitespace-only.
    /// </summary>
    private static FileEntryViewModel? FindNextWithChanges(
        IReadOnlyList<FileEntryViewModel> entries, int from, bool forward)
    {
        int n = entries.Count;
        for (int hop = 1; hop <= n; hop++)
        {
            int idx = forward
                ? (from + hop) % n
                : ((from - hop) % n + n) % n;
            var e = entries[idx];
            // null = pre-diff hasn't run yet; treat as eligible so the user
            // doesn't get stuck early in the load. Confirmed-false skips.
            if (e.HasVisibleDifferences ?? true) return e;
        }
        return null;
    }

    private bool TryGetHunkAction(HunkActionContext? ctx, out FileEntryViewModel entry, out HunkPatchInputs inputs)
    {
        entry = null!;
        inputs = null!;
        if (ctx is null || _gitWriteService is null) return false;

        var resolvedHunk = DiffPane.HunkAtLine(ctx.Side, ctx.OneBasedLine);
        if (resolvedHunk is null) return false;

        var built = DiffPane.BuildHunkPatchInputs(resolvedHunk);
        if (built is null || DiffPane.CurrentEntry is null) return false;

        entry = DiffPane.CurrentEntry;
        inputs = built;
        return true;
    }

    /// <summary>
    /// Set the clipboard via the View. Lifted out so tests / headless
    /// contexts can override; defaults to <c>System.Windows.Clipboard</c>.
    /// </summary>
    public Action<string>? ClipboardWriter { get; set; }

    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (ClipboardWriter is not null)
        {
            ClipboardWriter(text);
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            ToastHandler?.Invoke($"Clipboard copy failed: {ex.Message}");
        }
    }

    public MainViewModel(
        IRepositoryService repository,
        DiffSide left,
        DiffSide right,
        IDiffService? diffService = null,
        IRepositoryWatcher? watcher = null,
        IPreDiffPass? preDiffPass = null,
        ISettingsService? settingsService = null,
        IGitWriteService? gitWriteService = null,
        IExternalAppLauncher? externalAppLauncher = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _isCommitVsCommit = left is DiffSide.CommitIsh && right is DiffSide.CommitIsh;
        _watcher = watcher;
        _preDiffPass = preDiffPass;
        _settingsService = settingsService;
        _gitWriteService = gitWriteService;
        _externalAppLauncher = externalAppLauncher;

        FileList = new FileListViewModel(settingsService);
        DiffPane = new DiffPaneViewModel(_repository, diffService, _isCommitVsCommit, settingsService);

        WindowTitle = $"DiffViewer — {repository.Shape.RepoRoot} ({FormatSideForTitle(left)} ⇢ {FormatSideForTitle(right)})";

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
    /// Format a <see cref="DiffSide"/> for inclusion in the window title.
    /// Renders <c>WorkingTree</c> as the human-friendly "working tree" string
    /// rather than the bracketed sentinel <c>&lt;working-tree&gt;</c>, which
    /// would clutter the title bar.
    /// </summary>
    private static string FormatSideForTitle(DiffSide side) => side switch
    {
        DiffSide.WorkingTree => "working tree",
        DiffSide.CommitIsh c => c.Reference,
        _ => side.ToString() ?? "?",
    };

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
