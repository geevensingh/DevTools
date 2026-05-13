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
    private readonly bool _isCommitVsCommit;
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

    public DiffPaneViewModel(IRepositoryService repository, IDiffService? diffService = null, bool isCommitVsCommit = false)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _diffService = diffService;
        _isCommitVsCommit = isCommitVsCommit;
    }

    /// <summary>
    /// Build the immutable <see cref="DiffOptions"/> consumed by
    /// <see cref="IDiffService"/> from the toolbar's observable state.
    /// </summary>
    public DiffOptions BuildDiffOptions() => new(IgnoreWhitespace: IgnoreWhitespace);

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
            return Task.CompletedTask;
        }

        var change = entry.Change;
        var earlyPlaceholder = ResolvePlaceholderForShape(change);
        if (earlyPlaceholder is not null)
        {
            ApplyResult(string.Empty, string.Empty, earlyPlaceholder,
                Array.Empty<DiffHunk>(), DiffHighlightMap.Empty, InlineDiffBuilder.Build(Array.Empty<DiffHunk>()), false);
            return Task.CompletedTask;
        }

        IsLoading = true;
        var options = BuildDiffOptions();
        bool intraLine = ShowIntraLineDiff;

        return Task.Run(() =>
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
        var inline = InlineDiffBuilder.Build(computation.Hunks);

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
    }

    partial void OnIgnoreWhitespaceChanged(bool value) => ScheduleOptionRefresh();
    partial void OnShowIntraLineDiffChanged(bool value) => ScheduleOptionRefresh();

    partial void OnLiveUpdatesChanged(bool value) =>
        OnPropertyChanged(nameof(LiveUpdatesEffective));

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
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _optionDebounceTimer?.Stop();
    }
}

public sealed record HunkNavigationEventArgs(int HunkIndex, int LeftLine, int RightLine);
