using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Rendering;
using DiffViewer.Services;
using ICSharpCode.AvalonEdit.Document;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the right-pane view. Owns the side-by-side documents
/// (<see cref="LeftDocument"/> / <see cref="RightDocument"/>), the inline
/// document used by the inline-mode editor, the toolbar toggle state, the
/// per-side line highlight maps, and the whitespace-only banner.
/// </summary>
public sealed partial class DiffPaneViewModel : ObservableObject, IDisposable
{
    private readonly IRepositoryService _repository;
    private readonly IDiffService? _diffService;
    private readonly ISettingsService? _settingsService;
    private readonly bool _isCommitVsCommit;
    private bool _suppressSettingsWrite;
    private CancellationTokenSource? _loadCts;

    // Cached blobs from the last successful load - lets us recompute the
    // highlight map / inline document on a toolbar option change without
    // re-reading the blobs.
    private FileEntryViewModel? _currentEntry;
    private string _cachedLeftText = string.Empty;
    private string _cachedRightText = string.Empty;
    private IReadOnlyList<DiffHunk> _currentHunks = Array.Empty<DiffHunk>();
    private DispatcherTimer? _optionDebounceTimer;
    private const int OptionDebounceMs = 200;

    public TextDocument LeftDocument { get; } = new();
    public TextDocument RightDocument { get; } = new();
    public TextDocument InlineDocument { get; } = new();

    /// <summary>Per-side line highlights for the side-by-side view.</summary>
    public DiffHighlightMap HighlightMap { get; private set; } = DiffHighlightMap.Empty;

    /// <summary>Inline-mode line classification - one entry per added / removed line.</summary>
    public IReadOnlyDictionary<int, DiffLineKind> InlineLineKinds { get; private set; } =
        new Dictionary<int, DiffLineKind>();

    /// <summary>
    /// Raised on the UI thread after the highlight map / inline kinds are
    /// replaced. The view re-points renderers + redraws.
    /// </summary>
    public event EventHandler? HighlightMapChanged;

    /// <summary>
    /// Raised when <see cref="NavigateNextHunkCommand"/> /
    /// <see cref="NavigatePreviousHunkCommand"/> moves the cursor; the view
    /// scrolls its editors to the requested 1-based line numbers.
    /// </summary>
    public event EventHandler<HunkNavigationEventArgs>? HunkNavigationRequested;

    [ObservableProperty]
    private bool _isWhitespaceOnlyBannerVisible;

    [ObservableProperty]
    private string? _placeholderMessage = "Select a file to see its diff.";

    [ObservableProperty]
    private bool _isLoading;

    public bool ShowPlaceholder => PlaceholderMessage is not null;
    public bool ShowEditors => PlaceholderMessage is null;
    public bool ShowSideBySide => ShowEditors && IsSideBySide;
    public bool ShowInline => ShowEditors && !IsSideBySide;

    // ---- Toolbar toggle state ----

    [ObservableProperty] private bool _ignoreWhitespace;
    [ObservableProperty] private bool _showIntraLineDiff = true;
    [ObservableProperty] private bool _isSideBySide = true;
    [ObservableProperty] private bool _showVisibleWhitespace;

    /// <summary>
    /// User intent for live-updates. Greyed out via
    /// <see cref="IsLiveUpdatesAvailable"/> when both sides are commit-ish
    /// (the repo watcher is inactive in that mode anyway).
    /// </summary>
    [ObservableProperty] private bool _liveUpdates = true;

    /// <summary>True when at least one side is the working tree.</summary>
    public bool IsLiveUpdatesAvailable => !_isCommitVsCommit;

    /// <summary>Effective live-updates state (intent AND availability).</summary>
    public bool LiveUpdatesEffective => LiveUpdates && IsLiveUpdatesAvailable;

    /// <summary>The hunks for the currently-loaded file (empty if none).</summary>
    public IReadOnlyList<DiffHunk> CurrentHunks => _currentHunks;

    /// <summary>Last hunk visited via <see cref="NavigateNextHunkCommand"/> /
    /// <see cref="NavigatePreviousHunkCommand"/>. <c>-1</c> when none yet.</summary>
    [ObservableProperty]
    private int _currentHunkIndex = -1;

    public DiffPaneViewModel(
        IRepositoryService repository,
        IDiffService? diffService = null,
        bool isCommitVsCommit = false,
        ISettingsService? settingsService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _diffService = diffService;
        _isCommitVsCommit = isCommitVsCommit;
        _settingsService = settingsService;

        // Seed toolbar state from persisted settings if present. Suppress
        // the OnXxxChanged callbacks during the seed so we don't immediately
        // round-trip the same values back to disk.
        if (_settingsService is not null)
        {
            _suppressSettingsWrite = true;
            try
            {
                var s = _settingsService.Current;
                IgnoreWhitespace = s.IgnoreWhitespace;
                ShowIntraLineDiff = s.ShowIntraLineDiff;
                IsSideBySide = s.IsSideBySide;
                ShowVisibleWhitespace = s.ShowVisibleWhitespace;
                LiveUpdates = s.LiveUpdates;
                FontSize = s.FontSize;
                CurrentColorScheme = DiffColorScheme.From(s.ColorScheme);
            }
            finally { _suppressSettingsWrite = false; }

            _settingsService.Changed += OnSettingsChanged;
        }
    }

    /// <summary>
    /// Resolved diff color scheme from current settings. Replaced (and
    /// <see cref="ColorSchemeChanged"/> raised) whenever the user picks a
    /// new preset in the Settings dialog or hand-edits the JSON.
    /// </summary>
    public DiffColorScheme CurrentColorScheme { get; private set; } = DiffColorScheme.Classic;

    /// <summary>
    /// Raised on the UI thread after <see cref="CurrentColorScheme"/> has
    /// been swapped. The view rebuilds its background renderers and
    /// colorizers with the new palette.
    /// </summary>
    public event EventHandler? ColorSchemeChanged;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!Equals(e.Previous.ColorScheme, e.Current.ColorScheme))
        {
            CurrentColorScheme = DiffColorScheme.From(e.Current.ColorScheme);
            ColorSchemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Default editor font size when no settings service is wired (mirrors
    // AppSettings.FontSize default). Editor controls bind to FontSize.
    [ObservableProperty] private double _fontSize = 11.0;

    /// <summary>Lower clamp for <see cref="FontSize"/>. Below this AvalonEdit's
    /// rendering becomes unusable. Matches the Settings dialog's input range.</summary>
    public const double MinFontSize = 6.0;

    /// <summary>Upper clamp for <see cref="FontSize"/>. Above this WPF text
    /// formatting becomes glitchy. Matches the Settings dialog.</summary>
    public const double MaxFontSize = 72.0;

    partial void OnFontSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, MinFontSize, MaxFontSize);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            // Re-entrant: setting the property here triggers another
            // OnFontSizeChanged with the clamped value, which then no-ops.
            FontSize = clamped;
            return;
        }
        if (_settingsService is not null && !_suppressSettingsWrite)
        {
            _settingsService.Update(s => s with { FontSize = value });
        }
    }

    [RelayCommand]
    private void ZoomIn()  => FontSize = Math.Min(MaxFontSize, FontSize + 1.0);

    [RelayCommand]
    private void ZoomOut() => FontSize = Math.Max(MinFontSize, FontSize - 1.0);

    [RelayCommand]
    private void ZoomReset() => FontSize = 11.0;

    /// <summary>
    /// Build the immutable <see cref="DiffOptions"/> consumed by
    /// <see cref="IDiffService"/> from the toolbar's observable state.
    /// </summary>
    public DiffOptions BuildDiffOptions() => new(IgnoreWhitespace: IgnoreWhitespace);

    /// <summary>
    /// Most-recent in-flight load Task. Cross-file navigation orchestrators
    /// (e.g. <see cref="MainViewModel.NavigateNextChange"/>) await this
    /// before issuing a follow-up <see cref="JumpToFirstHunk"/> so the
    /// jump lands after the new file's hunks are populated.
    /// </summary>
    public Task LastLoadTask { get; private set; } = Task.CompletedTask;

    public Task LoadAsync(FileEntryViewModel? entry)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _currentEntry = entry;

        if (entry is null)
        {
            ApplyResult(string.Empty, string.Empty, "Select a file to see its diff.",
                Array.Empty<DiffHunk>(), DiffHighlightMap.Empty, InlineDiffBuilder.Build(Array.Empty<DiffHunk>()), false);
            LastLoadTask = Task.CompletedTask;
            return LastLoadTask;
        }

        var change = entry.Change;
        var earlyPlaceholder = ResolvePlaceholderForShape(change)
            ?? ResolveLargeFilePlaceholder(change);
        if (earlyPlaceholder is not null)
        {
            ApplyResult(string.Empty, string.Empty, earlyPlaceholder,
                Array.Empty<DiffHunk>(), DiffHighlightMap.Empty, InlineDiffBuilder.Build(Array.Empty<DiffHunk>()), false);
            LastLoadTask = Task.CompletedTask;
            return LastLoadTask;
        }

        IsLoading = true;
        var options = BuildDiffOptions();
        bool intraLine = ShowIntraLineDiff;

        var task = Task.Run(() =>
        {
            string left = SafeReadSide(change, ChangeSide.Left);
            string right = SafeReadSide(change, ChangeSide.Right);
            var (hunks, map, inline, ws) = ComputeDiffArtifacts(left, right, options, intraLine);
            return (left, right, hunks, map, inline, ws);
        }, ct).ContinueWith(t =>
        {
            if (ct.IsCancellationRequested) return;

            if (t.IsFaulted)
            {
                ApplyResult(
                    string.Empty,
                    string.Empty,
                    $"Failed to read blobs: {t.Exception?.GetBaseException().Message}",
                    Array.Empty<DiffHunk>(),
                    DiffHighlightMap.Empty,
                    InlineDiffBuilder.Build(Array.Empty<DiffHunk>()),
                    false);
            }
            else
            {
                var (left, right, hunks, map, inline, ws) = t.Result;
                _cachedLeftText = left;
                _cachedRightText = right;
                ApplyResult(left, right, null, hunks, map, inline, ws);
            }

            IsLoading = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());

        LastLoadTask = task;
        return task;
    }

    /// <summary>
    /// Recompute the diff artifacts from the cached blobs under the current
    /// toolbar state. Used after a toggle change so we don't re-read blobs.
    /// </summary>
    private void RefreshDiffFromCache()
    {
        if (_currentEntry is null || _diffService is null || PlaceholderMessage is not null) return;

        var options = BuildDiffOptions();
        bool intraLine = ShowIntraLineDiff;
        var (hunks, map, inline, ws) = ComputeDiffArtifacts(_cachedLeftText, _cachedRightText, options, intraLine);
        ApplyResult(_cachedLeftText, _cachedRightText, null, hunks, map, inline, ws);
    }

    private (IReadOnlyList<DiffHunk> Hunks, DiffHighlightMap Map, InlineDiffBuilder.InlineDocument Inline, bool WhitespaceOnly)
        ComputeDiffArtifacts(string left, string right, DiffOptions options, bool intraLineEnabled)
    {
        if (_diffService is null)
        {
            return (Array.Empty<DiffHunk>(), DiffHighlightMap.Empty,
                InlineDiffBuilder.Build(Array.Empty<DiffHunk>()), false);
        }

        var computation = _diffService.ComputeDiff(left, right, options);
        var map = DiffHighlightMap.FromHunks(
            computation.Hunks, _diffService, intraLineEnabled, options.IgnoreWhitespace);
        // Inline-mode view shows the FULL file with hunks woven in (not the
        // 3-line-context summary that Build emits) so the user can read the
        // surrounding code, matching what side-by-side already shows.
        var inline = InlineDiffBuilder.BuildFullFile(left, right, computation.Hunks);

        bool whitespaceOnly = false;
        if (options.IgnoreWhitespace && computation.Hunks.Count == 0)
        {
            whitespaceOnly = _diffService.HasVisibleDifferences(
                left, right, options with { IgnoreWhitespace = false });
        }

        return (computation.Hunks, map, inline, whitespaceOnly);
    }

    private static string? ResolvePlaceholderForShape(FileChange change)
    {
        if (change.IsLfsPointer)
            return "LFS object not fetched - run `git lfs pull` to populate.";
        if (change.IsSparseNotCheckedOut)
            return "Sparse checkout: this file is not present in the working tree.";
        if (change.IsBinary)
            return "Binary file - diff not displayed.";
        if (change.IsModeOnlyChange)
            return $"Mode change only: {Convert.ToString(change.OldMode, 8)} -> {Convert.ToString(change.NewMode, 8)}.";
        if (change.Status == Models.FileStatus.SubmoduleMoved)
            return $"Submodule moved {ShortSha(change.LeftBlobSha)} -> {ShortSha(change.RightBlobSha)}.";
        if (change.Status == Models.FileStatus.Conflicted)
            return "Conflicted file - 3-way view will be implemented in a later phase.";
        return null;
    }

    private string? ResolveLargeFilePlaceholder(FileChange change)
    {
        var threshold = _settingsService?.Current.LargeFileThresholdBytes ?? long.MaxValue;
        if (threshold <= 0) return null;

        long? larger = null;
        if (change.LeftFileSizeBytes is long l && l > threshold) larger = l;
        if (change.RightFileSizeBytes is long r && r > threshold &&
            (larger is null || r > larger.Value)) larger = r;
        if (larger is null) return null;

        return $"File too large to diff ({FormatBytes(larger.Value)}; threshold {FormatBytes(threshold)}). " +
               "Adjust the limit in Settings if needed.";
    }

    private static string FormatBytes(long bytes)
    {
        const long Mb = 1024L * 1024;
        const long Kb = 1024L;
        if (bytes >= Mb) return $"{bytes / (double)Mb:0.##} MB";
        if (bytes >= Kb) return $"{bytes / (double)Kb:0.##} KB";
        return $"{bytes} B";
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrEmpty(sha) ? "(none)" : sha.Length >= 7 ? sha[..7] : sha;

    private string SafeReadSide(FileChange change, ChangeSide side)
    {
        try
        {
            var blob = _repository.ReadSide(change, side);
            return blob.Text ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ApplyResult(
        string left,
        string right,
        string? placeholder,
        IReadOnlyList<DiffHunk> hunks,
        DiffHighlightMap highlightMap,
        InlineDiffBuilder.InlineDocument inline,
        bool whitespaceOnly)
    {
        if (Application.Current is { Dispatcher: { } d } && !d.CheckAccess())
        {
            d.BeginInvoke(() => ApplyResult(left, right, placeholder, hunks, highlightMap, inline, whitespaceOnly));
            return;
        }

        LeftDocument.Text = left;
        RightDocument.Text = right;
        InlineDocument.Text = inline.Text;
        PlaceholderMessage = placeholder;
        HighlightMap = highlightMap;
        InlineLineKinds = inline.LineKinds;
        _currentHunks = hunks;
        CurrentHunkIndex = -1;
        IsWhitespaceOnlyBannerVisible = whitespaceOnly;
        OnPropertyChanged(nameof(CurrentHunks));
        HighlightMapChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnPlaceholderMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(ShowEditors));
        OnPropertyChanged(nameof(ShowSideBySide));
        OnPropertyChanged(nameof(ShowInline));
    }

    partial void OnIsSideBySideChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSideBySide));
        OnPropertyChanged(nameof(ShowInline));
        PersistToolbarToSettings();
    }

    partial void OnIgnoreWhitespaceChanged(bool value)
    {
        ScheduleOptionRefresh();
        PersistToolbarToSettings();
    }

    partial void OnShowIntraLineDiffChanged(bool value)
    {
        ScheduleOptionRefresh();
        PersistToolbarToSettings();
    }

    partial void OnShowVisibleWhitespaceChanged(bool value) => PersistToolbarToSettings();

    partial void OnLiveUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(LiveUpdatesEffective));
        PersistToolbarToSettings();
    }

    private void PersistToolbarToSettings()
    {
        if (_settingsService is null || _suppressSettingsWrite) return;
        _settingsService.Update(s => s with
        {
            IgnoreWhitespace = IgnoreWhitespace,
            ShowIntraLineDiff = ShowIntraLineDiff,
            IsSideBySide = IsSideBySide,
            ShowVisibleWhitespace = ShowVisibleWhitespace,
            LiveUpdates = LiveUpdates,
        });
    }

    /// <summary>
    /// Coalesce rapid toggle clicks (e.g. spinning the same checkbox) into
    /// a single recompute. 200 ms is the standard "user stopped clicking"
    /// threshold from the plan's perf strategy.
    /// </summary>
    private void ScheduleOptionRefresh()
    {
        if (Application.Current is null)
        {
            // Test path - no Dispatcher, recompute synchronously.
            RefreshDiffFromCache();
            return;
        }

        if (_optionDebounceTimer is null)
        {
            _optionDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(OptionDebounceMs),
            };
            _optionDebounceTimer.Tick += (_, _) =>
            {
                _optionDebounceTimer!.Stop();
                RefreshDiffFromCache();
            };
        }

        _optionDebounceTimer.Stop();
        _optionDebounceTimer.Start();
    }

    [RelayCommand]
    private void NavigateNextHunk()
    {
        if (_currentHunks.Count == 0) return;

        int next = CurrentHunkIndex + 1;
        if (next >= _currentHunks.Count) next = 0; // cycle within file
        CurrentHunkIndex = next;
        RaiseHunkNav();
    }

    [RelayCommand]
    private void NavigatePreviousHunk()
    {
        if (_currentHunks.Count == 0) return;

        int prev = CurrentHunkIndex - 1;
        if (prev < 0) prev = _currentHunks.Count - 1;
        CurrentHunkIndex = prev;
        RaiseHunkNav();
    }

    /// <summary>
    /// Non-cycling step to the next hunk in the current file. Returns true
    /// if a step was made; false when the caret is already at (or past) the
    /// last hunk, signalling the orchestrator to advance to the next file.
    /// </summary>
    public bool TryNavigateNextHunkInFile()
    {
        if (_currentHunks.Count == 0) return false;
        int next = CurrentHunkIndex + 1;
        if (next >= _currentHunks.Count) return false;
        CurrentHunkIndex = next;
        RaiseHunkNav();
        return true;
    }

    /// <summary>Non-cycling step backwards (mirror of <see cref="TryNavigateNextHunkInFile"/>).</summary>
    public bool TryNavigatePreviousHunkInFile()
    {
        if (_currentHunks.Count == 0) return false;
        int prev = CurrentHunkIndex - 1;
        if (prev < 0) return false;
        CurrentHunkIndex = prev;
        RaiseHunkNav();
        return true;
    }

    /// <summary>True when caret/cursor is on the last visible hunk (or no hunks exist).</summary>
    public bool IsAtLastHunk =>
        _currentHunks.Count == 0 || CurrentHunkIndex >= _currentHunks.Count - 1;

    /// <summary>True when caret/cursor is on the first visible hunk (or no hunks exist).</summary>
    public bool IsAtFirstHunk =>
        _currentHunks.Count == 0 || CurrentHunkIndex <= 0;

    /// <summary>
    /// Move the caret to the first hunk in the current file, raising the
    /// navigation event. No-op when the file has no visible hunks.
    /// </summary>
    public void JumpToFirstHunk()
    {
        if (_currentHunks.Count == 0) return;
        CurrentHunkIndex = 0;
        RaiseHunkNav();
    }

    /// <summary>Move the caret to the last hunk in the current file.</summary>
    public void JumpToLastHunk()
    {
        if (_currentHunks.Count == 0) return;
        CurrentHunkIndex = _currentHunks.Count - 1;
        RaiseHunkNav();
    }

    private void RaiseHunkNav()
    {
        if (CurrentHunkIndex < 0 || CurrentHunkIndex >= _currentHunks.Count) return;
        var hunk = _currentHunks[CurrentHunkIndex];
        HunkNavigationRequested?.Invoke(this, new HunkNavigationEventArgs(
            HunkIndex: CurrentHunkIndex,
            LeftLine: hunk.OldStartLine,
            RightLine: hunk.NewStartLine));
    }

    public void Dispose()
    {
        if (_settingsService is not null)
        {
            _settingsService.Changed -= OnSettingsChanged;
        }
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _optionDebounceTimer?.Stop();
    }

    /// <summary>
    /// Returns the hunk that contains the supplied 1-based caret line, or
    /// <c>null</c> if the caret is in a pure-context region. Side picks
    /// which line numbering to use (left = old, right / inline = new).
    /// </summary>
    public DiffHunk? HunkAtLine(ChangeSide side, int oneBasedLine)
    {
        if (oneBasedLine < 1) return null;
        foreach (var h in _currentHunks)
        {
            int start = side == ChangeSide.Left ? h.OldStartLine : h.NewStartLine;
            int count = side == ChangeSide.Left ? h.OldLineCount : h.NewLineCount;
            if (count == 0)
            {
                // Pure-insert (count=0 on left) / pure-delete (count=0 on
                // right) hunks anchor at start; caret is "in" the hunk if
                // it sits exactly on the anchor line.
                if (oneBasedLine == start) return h;
            }
            else if (oneBasedLine >= start && oneBasedLine < start + count)
            {
                return h;
            }
        }
        return null;
    }

    /// <summary>
    /// Build the inputs for an <see cref="IGitWriteService"/> hunk apply
    /// against the current file. Returns null if no file is loaded or the
    /// underlying file is in a placeholder layer.
    /// </summary>
    public HunkPatchInputs? BuildHunkPatchInputs(DiffHunk hunk)
    {
        if (_currentEntry is null) return null;
        return new HunkPatchInputs(
            FilePath: _currentEntry.Change.Path,
            Hunk: hunk,
            LeftSource: _cachedLeftText,
            RightSource: _cachedRightText);
    }

    /// <summary>The file currently shown in the diff pane (null if none).</summary>
    public FileEntryViewModel? CurrentEntry => _currentEntry;

    /// <summary>
    /// Updates the per-pane "what was right-clicked" snapshot and the
    /// derived visibility flags below. Called by the View on
    /// <c>ContextMenuOpening</c> for each editor.
    /// </summary>
    public void UpdateRightClickContext(HunkActionContext ctx)
    {
        RightClickContext = ctx;
        var hunk = HunkAtLine(ctx.Side, ctx.OneBasedLine);
        var layer = _currentEntry?.Change.Layer;

        IsCaretInHunk = hunk is not null;
        CanStageHunkAtCaret  = hunk is not null && layer == WorkingTreeLayer.Unstaged;
        CanUnstageHunkAtCaret = hunk is not null && layer == WorkingTreeLayer.Staged;
        CanRevertHunkAtCaret  = hunk is not null && layer == WorkingTreeLayer.Unstaged;
    }

    /// <summary>Most recent right-click snapshot; bound by MenuItem.CommandParameter.</summary>
    [ObservableProperty]
    private HunkActionContext? _rightClickContext;

    [ObservableProperty] private bool _isCaretInHunk;
    [ObservableProperty] private bool _canStageHunkAtCaret;
    [ObservableProperty] private bool _canUnstageHunkAtCaret;
    [ObservableProperty] private bool _canRevertHunkAtCaret;
}

public sealed record HunkNavigationEventArgs(int HunkIndex, int LeftLine, int RightLine);
