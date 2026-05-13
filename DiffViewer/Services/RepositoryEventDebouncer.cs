using System.Threading;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Pure debounce + suspend logic shared by the production
/// <see cref="RepositoryWatcher"/> and the unit tests. The class knows
/// nothing about <c>FileSystemWatcher</c>: callers translate raw OS
/// events into <see cref="OnRawEvent"/> calls, and the debouncer batches
/// them into a single <see cref="Fired"/> invocation per quiescent window.
///
/// <para>
/// Suspend tokens compose. If raw events arrive while suspended, the
/// accumulated <see cref="RepositoryChangeKind"/> bitmask survives the
/// suspension and one <see cref="Fired"/> fires immediately when the
/// outermost token is disposed. <see cref="RepositoryChangeKind.BufferOverflow"/>
/// always bypasses the debounce timer (overflow is its own self-contained
/// signal that demands an immediate full refresh).
/// </para>
/// </summary>
public sealed class RepositoryEventDebouncer : IDisposable
{
    private readonly TimeSpan _debounceInterval;
    private readonly Action<RepositoryChangeKind> _onFire;
    private readonly Timer _timer;
    private readonly object _lock = new();

    private int _pendingKind;
    private int _suspendCount;
    private bool _hasPendingFireDuringSuspend;
    private bool _disposed;

    public RepositoryEventDebouncer(TimeSpan debounceInterval, Action<RepositoryChangeKind> onFire)
    {
        if (debounceInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounceInterval), "Must be positive.");

        _debounceInterval = debounceInterval;
        _onFire = onFire ?? throw new ArgumentNullException(nameof(onFire));
        _timer = new Timer(OnTimerTick, state: null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Record a raw event. Restarts the debounce timer (or fires immediately
    /// for <see cref="RepositoryChangeKind.BufferOverflow"/>). No-op if
    /// disposed.
    /// </summary>
    public void OnRawEvent(RepositoryChangeKind kind)
    {
        if (kind == RepositoryChangeKind.None) return;

        bool fireImmediately = false;
        RepositoryChangeKind kindToFire = RepositoryChangeKind.None;

        lock (_lock)
        {
            if (_disposed) return;

            _pendingKind |= (int)kind;

            if ((kind & RepositoryChangeKind.BufferOverflow) != 0)
            {
                if (_suspendCount > 0)
                {
                    _hasPendingFireDuringSuspend = true;
                }
                else
                {
                    kindToFire = (RepositoryChangeKind)_pendingKind;
                    _pendingKind = 0;
                    fireImmediately = true;
                }
            }
            else
            {
                _timer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
            }
        }

        if (fireImmediately) _onFire(kindToFire);
    }

    /// <summary>
    /// Suppress the debounced fire until the returned token is disposed.
    /// Tokens nest. The watcher continues to record events while suspended;
    /// when the last token is disposed, one fire happens immediately if
    /// any events accumulated.
    /// </summary>
    public IDisposable Suspend()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RepositoryEventDebouncer));
            _suspendCount++;
        }
        return new SuspensionToken(this);
    }

    private void Resume()
    {
        bool fireNow = false;
        RepositoryChangeKind kindToFire = RepositoryChangeKind.None;

        lock (_lock)
        {
            if (_suspendCount == 0) return; // double-dispose guard
            _suspendCount--;
            if (_suspendCount > 0) return;

            if (_hasPendingFireDuringSuspend && _pendingKind != 0)
            {
                fireNow = true;
                kindToFire = (RepositoryChangeKind)_pendingKind;
                _pendingKind = 0;
                _hasPendingFireDuringSuspend = false;
            }
        }

        if (fireNow) _onFire(kindToFire);
    }

    private void OnTimerTick(object? _)
    {
        FireFromTimer();
    }

    private void FireFromTimer()
    {
        bool fireNow = false;
        RepositoryChangeKind kindToFire = RepositoryChangeKind.None;

        lock (_lock)
        {
            if (_disposed) return;

            if (_suspendCount > 0)
            {
                // Hold for resume.
                _hasPendingFireDuringSuspend = true;
                return;
            }

            if (_pendingKind == 0) return;
            kindToFire = (RepositoryChangeKind)_pendingKind;
            _pendingKind = 0;
            fireNow = true;
        }

        if (fireNow) _onFire(kindToFire);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        _timer.Dispose();
    }

    private sealed class SuspensionToken : IDisposable
    {
        private RepositoryEventDebouncer? _owner;

        public SuspensionToken(RepositoryEventDebouncer owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Resume();
        }
    }
}
