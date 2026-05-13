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
    public DiffPaneViewModel DiffPane { get; }

    [ObservableProperty]
    private string _windowTitle = "DiffViewer";

    public MainViewModel(IRepositoryService repository, DiffSide left, DiffSide right, IDiffService? diffService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _isCommitVsCommit = left is DiffSide.CommitIsh && right is DiffSide.CommitIsh;

        DiffPane = new DiffPaneViewModel(_repository, diffService);

        WindowTitle = $"DiffViewer — {repository.Shape.RepoRoot} ({left} ⇢ {right})";

        _repository.ChangeListUpdated += OnChangeListUpdated;
        FileList.PropertyChanged += OnFileListPropertyChanged;
    }

    /// <summary>Trigger the initial change-list load (called once at startup).</summary>
    public void LoadInitialChanges()
    {
        var changes = _repository.EnumerateChanges(_left, _right);
        FileList.LoadFromChanges(changes, _repository.Shape.RepoRoot, _isCommitVsCommit);
    }

    private void OnFileListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileListViewModel.SelectedEntry)) return;
        // Fire-and-forget: the VM serialises in-flight loads via its own CTS.
        _ = DiffPane.LoadAsync(FileList.SelectedEntry);
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
        FileList.PropertyChanged -= OnFileListPropertyChanged;
        _repository.ChangeListUpdated -= OnChangeListUpdated;
        DiffPane.Dispose();
        _repository.Dispose();
    }
}
