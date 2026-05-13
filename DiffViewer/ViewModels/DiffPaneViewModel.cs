using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;
using ICSharpCode.AvalonEdit.Document;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the right-pane skeleton view: two AvalonEdit documents (left/right
/// blob text) plus a placeholder message for binary / LFS / submodule /
/// mode-only / sparse-not-checked-out / load-error cases. Hunk rendering,
/// the toolbar toggles, and the whitespace-only banner are added in later
/// phases — this VM only owns the raw text + placeholder state.
/// </summary>
public sealed partial class DiffPaneViewModel : ObservableObject, IDisposable
{
    private readonly IRepositoryService _repository;
    private CancellationTokenSource? _loadCts;

    public TextDocument LeftDocument { get; } = new();
    public TextDocument RightDocument { get; } = new();

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

    public DiffPaneViewModel(IRepositoryService repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
            ApplyResult(string.Empty, string.Empty, "Select a file to see its diff.");
            return Task.CompletedTask;
        }

        var change = entry.Change;
        var earlyPlaceholder = ResolvePlaceholderForShape(change);
        if (earlyPlaceholder is not null)
        {
            ApplyResult(string.Empty, string.Empty, earlyPlaceholder);
            return Task.CompletedTask;
        }

        IsLoading = true;
        return Task.Run(() =>
        {
            string left = SafeReadSide(change, ChangeSide.Left);
            string right = SafeReadSide(change, ChangeSide.Right);
            return (left, right);
        }, ct).ContinueWith(t =>
        {
            if (ct.IsCancellationRequested) return;

            if (t.IsFaulted)
            {
                ApplyResult(string.Empty, string.Empty, $"Failed to read blobs: {t.Exception?.GetBaseException().Message}");
            }
            else
            {
                var (left, right) = t.Result;
                ApplyResult(left, right, null);
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
    /// Apply the load result to the documents and the placeholder field.
    /// <see cref="TextDocument"/> is a <c>DispatcherObject</c> so the
    /// assignment must run on the UI thread; we marshal if necessary.
    /// </summary>
    private void ApplyResult(string left, string right, string? placeholder)
    {
        if (Application.Current is { Dispatcher: { } d } && !d.CheckAccess())
        {
            d.BeginInvoke(() => ApplyResult(left, right, placeholder));
            return;
        }

        LeftDocument.Text = left;
        RightDocument.Text = right;
        PlaceholderMessage = placeholder;
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
