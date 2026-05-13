using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Rendering;
using DiffViewer.Services;
using ICSharpCode.AvalonEdit.Document;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the right-pane view: two AvalonEdit documents (left/right blob
/// text), a placeholder message for binary / LFS / submodule / mode-only /
/// sparse-not-checked-out / load-error cases, the per-side line highlight
/// maps consumed by <see cref="DiffBackgroundRenderer"/> and
/// <see cref="IntraLineColorizer"/>, and the whitespace-only banner.
/// </summary>
public sealed partial class DiffPaneViewModel : ObservableObject, IDisposable
{
    private readonly IRepositoryService _repository;
    private readonly IDiffService? _diffService;
    private CancellationTokenSource? _loadCts;

    public TextDocument LeftDocument { get; } = new();
    public TextDocument RightDocument { get; } = new();

    /// <summary>
    /// Per-side line highlights for the currently-loaded file. Refreshed
    /// after every load (and, in later phases, every toolbar option change).
    /// The view subscribes to <c>HighlightMapChanged</c> to refresh its
    /// renderers.
    /// </summary>
    public DiffHighlightMap HighlightMap { get; private set; } = DiffHighlightMap.Empty;

    /// <summary>
    /// Raised on the UI thread after <see cref="HighlightMap"/> is replaced.
    /// The view re-points its renderers and triggers a redraw.
    /// </summary>
    public event EventHandler? HighlightMapChanged;

    [ObservableProperty]
    private bool _isWhitespaceOnlyBannerVisible;

    [ObservableProperty]
    private DiffOptions _diffOptions = new();

    /// <summary>
    /// Non-null when the diff pane should show its placeholder chrome instead
    /// of the two text editors (binary, LFS, submodule, mode-only, sparse,
    /// load error, or "no file selected").
    /// </summary>
    [ObservableProperty]
    private string? _placeholderMessage = "Select a file to see its diff.";

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when <see cref="PlaceholderMessage"/> is set.</summary>
    public bool ShowPlaceholder => PlaceholderMessage is not null;

    /// <summary>True when both editors should be visible (no placeholder).</summary>
    public bool ShowEditors => PlaceholderMessage is null;

    public DiffPaneViewModel(IRepositoryService repository, IDiffService? diffService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _diffService = diffService;
    }

    /// <summary>
    /// Load (or clear) the two documents for the supplied entry. Cancels any
    /// in-flight load. Safe to call from any thread.
    /// </summary>
    public Task LoadAsync(FileEntryViewModel? entry)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (entry is null)
        {
            ApplyResult(string.Empty, string.Empty, "Select a file to see its diff.", DiffHighlightMap.Empty, false);
            return Task.CompletedTask;
        }

        var change = entry.Change;
        var earlyPlaceholder = ResolvePlaceholderForShape(change);
        if (earlyPlaceholder is not null)
        {
            ApplyResult(string.Empty, string.Empty, earlyPlaceholder, DiffHighlightMap.Empty, false);
            return Task.CompletedTask;
        }

        IsLoading = true;
        var options = DiffOptions;
        return Task.Run(() =>
        {
            string left = SafeReadSide(change, ChangeSide.Left);
            string right = SafeReadSide(change, ChangeSide.Right);

            DiffHighlightMap map = DiffHighlightMap.Empty;
            bool whitespaceOnly = false;

            if (_diffService is not null)
            {
                var computation = _diffService.ComputeDiff(left, right, options);
                map = DiffHighlightMap.FromHunks(
                    computation.Hunks,
                    _diffService,
                    enableIntraLine: true,
                    ignoreWhitespace: options.IgnoreWhitespace);

                // Whitespace-only banner: zero visible hunks under current
                // options, but a re-probe with whitespace honoured shows
                // there *would* be differences without the filter.
                if (options.IgnoreWhitespace && computation.Hunks.Count == 0)
                {
                    whitespaceOnly = _diffService.HasVisibleDifferences(
                        left, right, options with { IgnoreWhitespace = false });
                }
            }

            return (left, right, map, whitespaceOnly);
        }, ct).ContinueWith(t =>
        {
            if (ct.IsCancellationRequested) return;

            if (t.IsFaulted)
            {
                ApplyResult(
                    string.Empty,
                    string.Empty,
                    $"Failed to read blobs: {t.Exception?.GetBaseException().Message}",
                    DiffHighlightMap.Empty,
                    false);
            }
            else
            {
                var (left, right, map, ws) = t.Result;
                ApplyResult(left, right, null, map, ws);
            }

            IsLoading = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Map the file-change shape to a placeholder message; returns <c>null</c>
    /// if the file should render in the two-editor view.
    /// </summary>
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

    /// <summary>
    /// Apply the load result to the documents, highlight map, banner and
    /// placeholder fields. <see cref="TextDocument"/> is a
    /// <c>DispatcherObject</c> so the assignment must run on the UI
    /// thread; we marshal if necessary.
    /// </summary>
    private void ApplyResult(
        string left,
        string right,
        string? placeholder,
        DiffHighlightMap highlightMap,
        bool whitespaceOnly)
    {
        if (Application.Current is { Dispatcher: { } d } && !d.CheckAccess())
        {
            d.BeginInvoke(() => ApplyResult(left, right, placeholder, highlightMap, whitespaceOnly));
            return;
        }

        LeftDocument.Text = left;
        RightDocument.Text = right;
        PlaceholderMessage = placeholder;
        HighlightMap = highlightMap;
        IsWhitespaceOnlyBannerVisible = whitespaceOnly;
        HighlightMapChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnPlaceholderMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(ShowEditors));
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
