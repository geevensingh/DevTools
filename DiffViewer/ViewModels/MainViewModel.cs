using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Root view-model. Owns the repository service, the file list, and the
/// (still-skeleton) right-pane state. Constructed by <c>CompositionRoot</c>
/// once command-line parsing succeeds.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IRepositoryService _repository;
    private readonly DiffSide _left;
    private readonly DiffSide _right;
    private readonly bool _isCommitVsCommit;

    public FileListViewModel FileList { get; } = new();

    [ObservableProperty]
    private string _windowTitle = "DiffViewer";

    public MainViewModel(IRepositoryService repository, DiffSide left, DiffSide right)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _isCommitVsCommit = left is DiffSide.CommitIsh && right is DiffSide.CommitIsh;

        WindowTitle = $"DiffViewer — {repository.Shape.RepoRoot} ({left} ⇢ {right})";

        _repository.ChangeListUpdated += OnChangeListUpdated;
    }

    /// <summary>Trigger the initial change-list load (called once at startup).</summary>
    public void LoadInitialChanges()
    {
        var changes = _repository.EnumerateChanges(_left, _right);
        FileList.LoadFromChanges(changes, _repository.Shape.RepoRoot, _isCommitVsCommit);
    }

    private void OnChangeListUpdated(object? sender, ChangeListUpdatedEventArgs e)
    {
        // Hop to UI thread - the watcher / write-op refresh paths fire from background threads.
        if (System.Windows.Application.Current is { Dispatcher: { } d } && !d.CheckAccess())
        {
            d.BeginInvoke(() => FileList.LoadFromChanges(e.Changes, _repository.Shape.RepoRoot, _isCommitVsCommit));
            return;
        }

        FileList.LoadFromChanges(e.Changes, _repository.Shape.RepoRoot, _isCommitVsCommit);
    }

    public void Dispose()
    {
        _repository.ChangeListUpdated -= OnChangeListUpdated;
        _repository.Dispose();
    }
}
