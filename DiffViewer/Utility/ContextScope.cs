using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DiffViewer.Utility;

/// <summary>
/// Per-context lifecycle owner. One <see cref="ContextScope"/> per
/// <see cref="ViewModels.MainViewModel"/>: it owns a linked
/// <see cref="CancellationTokenSource"/>, a registry of
/// <see cref="IDisposable"/> / <see cref="IAsyncDisposable"/> resources and
/// per-context cleanup callbacks, and a list of in-flight tasks that must
/// drain before the underlying resources are torn down.
///
/// <para><b>Disposal order</b> in <see cref="DisposeAsync"/>:</para>
/// <list type="number">
///   <item>Cancel the token (so all per-context async work observing
///         <see cref="Token"/> short-circuits).</item>
///   <item>Await all <see cref="TrackInFlight"/> tasks with a 5 s
///         timeout (cancellation / timeout exceptions are expected and
///         suppressed).</item>
///   <item>Run registered disposers in <em>reverse</em> registration
///         order, so subscriptions and dependent resources tear down
///         before the underlying services they depend on.</item>
///   <item>Dispose the linked CTS itself.</item>
/// </list>
///
/// <para>This single abstraction collapses what would otherwise be a
/// scatter of "remember to unsubscribe", "remember to drain in-flight
/// tasks", and "remember to dispose in the right order" notes across the
/// per-context graph (RepositoryService, RepositoryWatcher, PreDiffPass,
/// MainViewModel event subs, etc.).</para>
/// </summary>
public sealed class ContextScope : IAsyncDisposable
{
    private static readonly TimeSpan InFlightDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cts;
    private readonly List<Func<ValueTask>> _disposers = new();
    private readonly List<Task> _inFlight = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ContextScope() : this(CancellationToken.None) { }

    public ContextScope(CancellationToken appShutdownToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
    }

    /// <summary>
    /// Cancellation token observed by all per-context async work. Fires when
    /// <see cref="DisposeAsync"/> is called or when the linked app-shutdown
    /// token fires.
    /// </summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>True once <see cref="DisposeAsync"/> has begun.</summary>
    public bool IsDisposed
    {
        get { lock (_lock) return _disposed; }
    }

    /// <summary>Register an <see cref="IDisposable"/> resource for teardown in <see cref="DisposeAsync"/>.</summary>
    public void Register(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        AddDisposer(() => { resource.Dispose(); return default; });
    }

    /// <summary>Register an <see cref="IAsyncDisposable"/> resource for teardown in <see cref="DisposeAsync"/>.</summary>
    public void Register(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        AddDisposer(resource.DisposeAsync);
    }

    /// <summary>
    /// Register a synchronous cleanup callback (typically an event-
    /// subscription teardown). Runs at the same step as
    /// <see cref="Register(IDisposable)"/>.
    /// </summary>
    public void RegisterCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        AddDisposer(() => { cleanup(); return default; });
    }

    /// <summary>
    /// Track a long-running task so <see cref="DisposeAsync"/> can await it
    /// before tearing down the resources it may still be touching. Tasks
    /// already completed at registration are ignored.
    /// </summary>
    public void TrackInFlight(Task task)
    {
        if (task is null || task.IsCompleted) return;
        lock (_lock)
        {
            if (_disposed)
            {
                // Scope is already tearing down; nothing to do — the task
                // either observes Token (good) or doesn't (caller bug).
                return;
            }
            _inFlight.Add(task);
        }
    }

    private void AddDisposer(Func<ValueTask> disposer)
    {
        bool runImmediately = false;
        lock (_lock)
        {
            if (_disposed) runImmediately = true;
            else _disposers.Add(disposer);
        }

        if (runImmediately)
        {
            // Race: someone registered after dispose started. Best-effort
            // fire-and-forget — the caller is responsible for not
            // depending on this resource being kept alive.
            _ = disposer();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Task> inFlight;
        List<Func<ValueTask>> disposers;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            inFlight = new List<Task>(_inFlight);
            disposers = new List<Func<ValueTask>>(_disposers);
            _inFlight.Clear();
            _disposers.Clear();
        }

        try { _cts.Cancel(); } catch { /* already disposed */ }

        if (inFlight.Count > 0)
        {
            try
            {
                await Task.WhenAll(inFlight)
                    .WaitAsync(InFlightDrainTimeout)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Expected: cancellation, timeout, or task faults. We
                // intentionally swallow so disposers still run.
            }
        }

        for (int i = disposers.Count - 1; i >= 0; i--)
        {
            try { await disposers[i]().ConfigureAwait(false); }
            catch { /* best-effort; one bad disposer must not block others */ }
        }

        try { _cts.Dispose(); } catch { /* already disposed */ }
    }
}
