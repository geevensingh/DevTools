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

    /// <summary>Per-line highlights (kind + intra-line spans) for the inline view.</summary>
    public IReadOnlyDictionary<int, LineHighlight> InlineLineHighlights { get; private set; } =
        new Dictionary<int, LineHighlight>();

    /// <summary>
    /// Per-inline-output-line mapping back to <c>(OldLine, NewLine)</c> in
    /// the source buffers. Index 0 ↔ inline document line 1. Either side
    /// can be <c>null</c> for pure inserts / deletes. Drives the viewport
    /// indicator's projection of the inline editor's visible window onto
    /// the two-column overview bar.
    /// </summary>
    public IReadOnlyList<(int? OldLine, int? NewLine)> InlineLineToSourceLines { get; private set; } =
        Array.Empty<(int? OldLine, int? NewLine)>();

    /// <summary>
    /// Raised on the UI thread after the highlight map / inline kinds are
    /// replaced. The view re-points renderers + redraws.
    /// </summary>
    public event EventHandler? HighlightMapChanged;

    /// <summary>
    /// Raised when the cross-file navigation orchestrator (F7/F8 family on
    /// <see cref="MainViewModel"/>) moves the cursor to a hunk; the view
    /// scrolls its editors to the requested 1-based line numbers.
    /// </summary>
    public event EventHandler<HunkNavigationEventArgs>? HunkNavigationRequested;

    /// <summary>
    /// Raised when the user clicks the hunk overview bar's viewport band
    /// and wants the editors scrolled to that position. This is
    /// <em>scroll only</em> — unlike <see cref="HunkNavigationRequested"/>
    /// the handler must NOT move the caret or update
    /// <see cref="CurrentHunkIndex"/>. The two events are kept distinct
    /// so the bar can drive scroll-only interactions without re-tasking
    /// hunk navigation with a behaviour flag.
    /// </summary>
    public event EventHandler<ScrollRequestedEventArgs>? ScrollRequested;

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

    /// <summary>Last hunk visited via the cross-file F7/F8 navigation
    /// orchestrator (<see cref="MainViewModel.NavigateNextChangeCommand"/> /
    /// <see cref="MainViewModel.NavigatePreviousChangeCommand"/>) or a direct
    /// <see cref="JumpToHunk(int)"/> call from the overview bar.
    /// <c>-1</c> when none yet.</summary>
    [ObservableProperty]
    private int _currentHunkIndex = -1;

    /// <summary>
    /// Current editor viewport, projected onto each side's source line
    /// numbers. <c>null</c> before the first layout pass, when no file is
    /// loaded, or when the visible-lines collection is otherwise empty.
    /// Drives the viewport indicator on <see cref="DiffViewer.Rendering.HunkOverviewBar"/>;
    /// the bar subscribes via <see cref="System.ComponentModel.INotifyPropertyChanged"/>.
    /// </summary>
    [ObservableProperty]
    private ViewportState? _viewport;

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
                FontFamily = s.FontFamily;
                TabWidth = s.TabWidth;
                ShowLineNumbers = s.ShowLineNumbers;
                WordWrap = s.WordWrap;
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

        // Editor-appearance fields: push from settings into the
        // observable properties so the editors' bindings (and the
        // TabWidth bridge in DiffPaneView.xaml.cs) pick them up. The
        // suppress flag prevents the partial OnFontSizeChanged handler
        // from immediately writing the same value back to disk.
        _suppressSettingsWrite = true;
        try
        {
            if (!Equals(e.Previous.FontSize, e.Current.FontSize))
                FontSize = e.Current.FontSize;
            if (!string.Equals(e.Previous.FontFamily, e.Current.FontFamily, StringComparison.Ordinal))
                FontFamily = e.Current.FontFamily;
            if (e.Previous.TabWidth != e.Current.TabWidth)
                TabWidth = e.Current.TabWidth;
            if (e.Previous.ShowLineNumbers != e.Current.ShowLineNumbers)
                ShowLineNumbers = e.Current.ShowLineNumbers;
            if (e.Previous.WordWrap != e.Current.WordWrap)
                WordWrap = e.Current.WordWrap;
        }
        finally { _suppressSettingsWrite = false; }
    }

    // Default editor font size when no settings service is wired (mirrors
    // AppSettings.FontSize default). Editor controls bind to FontSize.
    [ObservableProperty] private double _fontSize = 11.0;

    /// <summary>
    /// Editor font-family name. Seeded from <see cref="AppSettings.FontFamily"/>
    /// and refreshed when the user changes the value in the Settings
    /// dialog. The three AvalonEdit panes bind their <c>FontFamily</c>
    /// dependency property to this; WPF's <c>FontFamilyConverter</c>
    /// resolves the string to an installed typeface.
    /// </summary>
    [ObservableProperty] private string _fontFamily = "Consolas";

    /// <summary>
    /// Editor tab width. Surfaced to AvalonEdit via
    /// <c>TextEditor.Options.IndentationSize</c> in the view's code-behind
    /// (<see cref="System.Windows.Controls.Control"/> doesn't expose a
    /// bindable tab-width DP).
    /// </summary>
    [ObservableProperty] private int _tabWidth = 4;

    /// <summary>Whether the gutter shows line numbers. Bound to each
    /// editor's <c>ShowLineNumbers</c> dependency property.</summary>
    [ObservableProperty] private bool _showLineNumbers = true;

    /// <summary>Whether long lines wrap at the editor's right edge.
    /// Bound to each editor's <c>WordWrap</c> dependency property.</summary>
    [ObservableProperty] private bool _wordWrap;

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
                Array.Empty<DiffHunk>(), DiffHighlightMap.Empty, InlineDiffBuilder.Empty, false);
            LastLoadTask = Task.CompletedTask;
            return LastLoadTask;
        }

        var change = entry.Change;
        var earlyPlaceholder = ResolvePlaceholderForShape(change)
            ?? ResolveLargeFilePlaceholder(change);
        if (earlyPlaceholder is not null)
        {
            ApplyResult(string.Empty, string.Empty, earlyPlaceholder,
                Array.Empty<DiffHunk>(), DiffHighlightMap.Empty, InlineDiffBuilder.Empty, false);
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
                    InlineDiffBuilder.Empty,
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
    /// Like <see cref="LoadAsync"/> but additionally jumps to the first hunk
    /// once the load completes. The jump is chained inside
    /// <see cref="LastLoadTask"/> itself, so any caller that awaits
    /// <see cref="LastLoadTask"/> also waits for the auto-jump to land. If
    /// the user has moved on to a different entry by the time the
    /// continuation fires, the jump is suppressed.
    /// </summary>
    public Task LoadAndScrollToFirstHunkAsync(FileEntryViewModel? entry)
    {
        LoadAsync(entry);
        var requested = entry;
        LastLoadTask = LastLoadTask.ContinueWith(_ =>
        {
            if (!ReferenceEquals(_currentEntry, requested)) return;
            JumpToFirstHunk();
        }, CancellationToken.None,
           TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
           TaskScheduler.FromCurrentSynchronizationContext());
        return LastLoadTask;
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
                InlineDiffBuilder.Empty, false);
        }

        var computation = _diffService.ComputeDiff(left, right, options);
        var map = DiffHighlightMap.FromHunks(
            computation.Hunks, _diffService, intraLineEnabled, options.IgnoreWhitespace);
        // Inline-mode view shows the FULL file with hunks woven in so the
        // user can read the surrounding code, matching what side-by-side
        // already shows. Lines are emitted verbatim (no +/- prefix) and the
        // map drives per-line tints + intra-line span colorization.
        var inline = InlineDiffBuilder.BuildFullFile(left, right, computation.Hunks, map);

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

        // Only mutate document text when it actually changed. Setting
        // TextDocument.Text to its current value still calls Replace under
        // the hood, which raises TextChanged and rebuilds visual lines -
        // perturbing editor state for no benefit on a refresh that produces
        // identical content (e.g. RepositoryWatcher fires but nothing in
        // this file actually changed). Skipping the no-op assignment keeps
        // the user's scroll position stable on same-content reloads.
        if (!string.Equals(LeftDocument.Text, left, StringComparison.Ordinal))
            LeftDocument.Text = left;
        if (!string.Equals(RightDocument.Text, right, StringComparison.Ordinal))
            RightDocument.Text = right;
        if (!string.Equals(InlineDocument.Text, inline.Text, StringComparison.Ordinal))
            InlineDocument.Text = inline.Text;
        PlaceholderMessage = placeholder;
        HighlightMap = highlightMap;
        InlineLineHighlights = inline.LineHighlights;
        InlineLineToSourceLines = inline.LineToSourceLines;

        // Preserve CurrentHunkIndex across reloads when the new hunks have
        // the same shape (count + per-hunk start lines / line counts) as
        // the old ones. Without this, a same-file refresh resets the index
        // to -1 even when the diff is structurally unchanged - so the next
        // F7/F8 press would start from hunk 0 instead of continuing from
        // where the user actually was. Reference equality on the hunk list
        // would always say "different" because we built a fresh list, and
        // record equality on DiffHunk would compare the per-hunk Lines
        // collection by reference (also always different); checking the
        // tuple (OldStartLine, OldLineCount, NewStartLine, NewLineCount)
        // captures the navigationally-relevant identity of each hunk.
        bool preserveHunkIndex =
            CurrentHunkIndex >= 0 &&
            CurrentHunkIndex < hunks.Count &&
            HunksHaveSameShape(_currentHunks, hunks);
        _currentHunks = hunks;
        if (!preserveHunkIndex)
        {
            CurrentHunkIndex = -1;
        }
        // Reset the viewport indicator — the new document will compute its
        // own visible-range on the next layout pass.
        Viewport = null;
        IsWhitespaceOnlyBannerVisible = whitespaceOnly;
        OnPropertyChanged(nameof(CurrentHunks));
        HighlightMapChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool HunksHaveSameShape(IReadOnlyList<DiffHunk> a, IReadOnlyList<DiffHunk> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.OldStartLine != y.OldStartLine ||
                x.OldLineCount != y.OldLineCount ||
                x.NewStartLine != y.NewStartLine ||
                x.NewLineCount != y.NewLineCount)
            {
                return false;
            }
        }
        return true;
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

    /// <summary>
    /// Move the caret to a specific hunk by index, raising the navigation
    /// event. Called from <see cref="DiffViewer.Rendering.HunkOverviewBar"/>
    /// when the user clicks a marker. Out-of-range indices are clamped to a
    /// no-op rather than throwing — the overview bar's hit-test math can
    /// briefly disagree with the VM's hunk list mid-reload.
    /// </summary>
    public void JumpToHunk(int index)
    {
        if (index < 0 || index >= _currentHunks.Count) return;
        CurrentHunkIndex = index;
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

    /// <summary>
    /// Request a scroll-only jump to <paramref name="yFraction"/> of the
    /// overview bar's height. The fraction is mapped onto each side's
    /// total line count and raised as <see cref="ScrollRequested"/>. The
    /// caret is <em>not</em> moved and <see cref="CurrentHunkIndex"/> is
    /// <em>not</em> updated — that's the whole reason this is a separate
    /// event from <see cref="HunkNavigationRequested"/>.
    /// </summary>
    public void RequestScrollByFraction(double yFraction)
    {
        if (double.IsNaN(yFraction)) return;
        if (yFraction < 0) yFraction = 0;
        if (yFraction > 1) yFraction = 1;
        int leftTotal = Math.Max(1, LeftDocument.LineCount);
        int rightTotal = Math.Max(1, RightDocument.LineCount);
        int leftLine = (int)Math.Round(yFraction * leftTotal);
        int rightLine = (int)Math.Round(yFraction * rightTotal);
        if (leftLine < 1) leftLine = 1;
        if (leftLine > leftTotal) leftLine = leftTotal;
        if (rightLine < 1) rightLine = 1;
        if (rightLine > rightTotal) rightLine = rightTotal;
        ScrollRequested?.Invoke(this, new ScrollRequestedEventArgs(leftLine, rightLine));
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

/// <summary>
/// Args for <see cref="DiffPaneViewModel.ScrollRequested"/>. Carries the
/// 1-based target line on each side, computed from the click position on
/// the overview bar. Intentionally does NOT carry a hunk index — the
/// viewport indicator is independent of hunk navigation.
/// </summary>
public sealed record ScrollRequestedEventArgs(int LeftLine, int RightLine);
