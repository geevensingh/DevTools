using DiffViewer.Models;
using DiffViewer.ViewModels;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IPreDiffPass"/>: bounded-concurrency worker pool
/// over a priority queue (selected entry first, rest FIFO).
/// </summary>
public sealed class PreDiffPass : IPreDiffPass
{
    /// <summary>Default ctor parallelism per the plan's perf strategy.</summary>
    public const int DefaultMaxConcurrency = 4;

    /// <summary>25 MB - the plan's resolved-design-decision threshold.</summary>
    public const long DefaultLargeFileThresholdBytes = 25L * 1024 * 1024;

    private readonly IRepositoryService _repository;
    private readonly IDiffService _diffService;
    private readonly int _maxConcurrency;
    private readonly Func<long> _getLargeFileThresholdBytes;
    private readonly Action<Action> _uiMarshaller;
    private readonly object _lock = new();

    private CancellationTokenSource? _passCts;
    private Task? _passTask;

    /// <summary>The single "next up" priority slot; takes precedence over the FIFO queue.</summary>
    private FileEntryViewModel? _priorityEntry;

    /// <summary>FIFO queue for everything else.</summary>
    private readonly LinkedList<FileEntryViewModel> _queue = new();

    /// <summary>Hash set mirror of <see cref="_queue"/> + <see cref="_priorityEntry"/> for O(1) Contains.</summary>
    private readonly HashSet<FileEntryViewModel> _pending = new();

    private DiffOptions _currentOptions = new();

    public PreDiffPass(
        IRepositoryService repository,
        IDiffService diffService,
        int maxConcurrency = DefaultMaxConcurrency,
        long largeFileThresholdBytes = DefaultLargeFileThresholdBytes,
        Action<Action>? uiMarshaller = null)
        : this(repository, diffService, maxConcurrency, () => largeFileThresholdBytes, uiMarshaller) { }

    public PreDiffPass(
        IRepositoryService repository,
        IDiffService diffService,
        int maxConcurrency,
        Func<long> getLargeFileThresholdBytes,
        Action<Action>? uiMarshaller = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diffService);
        ArgumentNullException.ThrowIfNull(getLargeFileThresholdBytes);
        if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        _repository = repository;
        _diffService = diffService;
        _maxConcurrency = maxConcurrency;
        _getLargeFileThresholdBytes = getLargeFileThresholdBytes;
        _uiMarshaller = uiMarshaller ?? DefaultUiMarshaller;
    }

    public void Start(
        IReadOnlyList<FileEntryViewModel> entries,
        FileEntryViewModel? selectedEntry,
        DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        CancelInternal();

        var newCts = new CancellationTokenSource();
        var ct = newCts.Token;

        lock (_lock)
        {
            _currentOptions = options;
            _queue.Clear();
            _pending.Clear();
            _priorityEntry = null;

            foreach (var entry in entries)
            {
                if (!ShouldDiff(entry)) continue;
                if (entry.HasVisibleDifferences is not null) continue; // already stamped
                _queue.AddLast(entry);
                _pending.Add(entry);
            }

            if (selectedEntry is not null && _pending.Contains(selectedEntry))
            {
                PromoteToPriorityLocked(selectedEntry);
            }

            _passCts = newCts;
        }

        _passTask = Task.Run(() => RunPassAsync(ct), ct);
    }

    public void OnSelectionChanged(FileEntryViewModel? newSelection)
    {
        if (newSelection is null) return;
        lock (_lock)
        {
            if (!_pending.Contains(newSelection)) return; // already done or skipped
            PromoteToPriorityLocked(newSelection);
        }
    }

    public void OnOptionsChanged(
        IReadOnlyList<FileEntryViewModel> entries,
        FileEntryViewModel? selectedEntry,
        DiffOptions newOptions)
    {
        // Wipe previously-stamped results - the option change may flip them.
        foreach (var entry in entries)
        {
            _uiMarshaller(() => entry.HasVisibleDifferences = null);
        }
        Start(entries, selectedEntry, newOptions);
    }

    public void Cancel() => CancelInternal();

    public void Dispose() => CancelInternal();

    // ---- internals ----

    private void CancelInternal()
    {
        CancellationTokenSource? toCancel;
        lock (_lock)
        {
            toCancel = _passCts;
            _passCts = null;
            _queue.Clear();
            _pending.Clear();
            _priorityEntry = null;
        }
        try
        {
            toCancel?.Cancel();
            toCancel?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Race with another concurrent Cancel() - safe to ignore.
        }
    }

    private void PromoteToPriorityLocked(FileEntryViewModel entry)
    {
        // If a previous priority is still pending, push it back to the head
        // of the FIFO queue so it stays "next up after the new selection".
        if (_priorityEntry is { } prev && prev != entry && _pending.Contains(prev))
        {
            _queue.AddFirst(prev);
        }
        _queue.Remove(entry); // no-op if not there
        _priorityEntry = entry;
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        // Spawn N worker loops; each worker dequeues + processes until the
        // queue is empty or cancellation fires. This pattern (vs. a single
        // dispatcher that pre-dequeues into a semaphore) is what makes
        // mid-pass reprioritization actually work: an entry promoted to
        // priority is only pulled from the queue at the moment a worker
        // is ready for its next task, never speculatively held in flight.
        var workers = new Task[_maxConcurrency];
        for (int i = 0; i < _maxConcurrency; i++)
        {
            workers[i] = Task.Run(() => WorkerLoopAsync(ct), ct);
        }
        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            FileEntryViewModel? entry = TryDequeue();
            if (entry is null) return;
            await ProcessEntryAsync(entry, ct).ConfigureAwait(false);
        }
    }

    private FileEntryViewModel? TryDequeue()
    {
        lock (_lock)
        {
            if (_priorityEntry is { } pe)
            {
                _priorityEntry = null;
                _pending.Remove(pe);
                return pe;
            }
            if (_queue.Count == 0) return null;
            var first = _queue.First!.Value;
            _queue.RemoveFirst();
            _pending.Remove(first);
            return first;
        }
    }

    private Task ProcessEntryAsync(FileEntryViewModel entry, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.CompletedTask;
        if (entry.HasVisibleDifferences is not null) return Task.CompletedTask;

        try
        {
            string left = SafeRead(entry.Change, ChangeSide.Left);
            string right = SafeRead(entry.Change, ChangeSide.Right);

            if (ct.IsCancellationRequested) return Task.CompletedTask;

            DiffOptions options;
            lock (_lock) { options = _currentOptions; }

            bool hasDiff = _diffService.HasVisibleDifferences(left, right, options);

            if (ct.IsCancellationRequested) return Task.CompletedTask;

            _uiMarshaller(() => entry.HasVisibleDifferences = hasDiff);
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Couldn't determine - leave HasVisibleDifferences null rather
            // than lying. The diff pane will surface the read error when
            // the user clicks the file.
        }

        return Task.CompletedTask;
    }

    private string SafeRead(FileChange change, ChangeSide side)
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

    private bool ShouldDiff(FileEntryViewModel entry)
    {
        var c = entry.Change;
        if (c.IsBinary || c.IsLfsPointer || c.IsSparseNotCheckedOut || c.IsModeOnlyChange) return false;
        if (c.Status == Models.FileStatus.SubmoduleMoved || c.Status == Models.FileStatus.Conflicted) return false;
        var threshold = _getLargeFileThresholdBytes();
        if (c.LeftFileSizeBytes is { } l && l > threshold) return false;
        if (c.RightFileSizeBytes is { } r && r > threshold) return false;
        return true;
    }

    private static void DefaultUiMarshaller(Action action)
    {
        if (System.Windows.Application.Current is { Dispatcher: { } d } && !d.CheckAccess())
        {
            d.BeginInvoke(action);
            return;
        }
        action();
    }

    /// <summary>Test-only handle to wait for the in-flight pass.</summary>
    internal Task? PassTask => _passTask;
}
