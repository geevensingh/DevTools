using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;

namespace DiffViewer;

/// <summary>
/// Owns the <see cref="MainViewModel"/> lifecycle for a single window
/// session: cold-launch (parse args + build context), in-place switch
/// (build new context + atomic swap + dispose outgoing), and shutdown
/// (dispose current).
///
/// <para>The coordinator is the only place that decides "is the app
/// currently switching?", "do we shut down on launch failure?", and
/// "in what order do swap / dispose happen?". Wiring everything through
/// here makes the answer testable instead of scattered across <c>App.xaml.cs</c>
/// and the view-model.</para>
///
/// <para><b>Thread expectation</b>: public methods are intended to be
/// called from the UI thread. Internal awaits use
/// <c>ConfigureAwait(true)</c> so resumption stays on the calling
/// SynchronizationContext (the WPF dispatcher in production).</para>
/// </summary>
public sealed class MainWindowCoordinator : ObservableObject, IContextSwitcher
{
    /// <summary>
    /// Test seam for the per-context build step. Defaults to
    /// <see cref="CompositionRoot.BuildContextAsync"/> in production.
    /// </summary>
    public delegate Task<MainViewModel> ContextFactory(
        ParsedCommandLine parsed,
        AppServices services,
        ContextScope scope,
        CancellationToken ct);

    private readonly AppServices _services;
    private readonly IDialogService _dialog;
    private readonly CancellationToken _appShutdownToken;
    private readonly ContextFactory _contextFactory;
    private readonly Action<int>? _shutdownAction;
    private readonly SemaphoreSlim _switchGate = new(1, 1);

    private IShellViewModel? _current;
    private ContextScope? _currentScope;
    private bool _isSwitching;

    public MainWindowCoordinator(
        AppServices services,
        IDialogService dialog,
        CancellationToken appShutdownToken = default,
        ContextFactory? contextFactory = null,
        Action<int>? shutdownAction = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _appShutdownToken = appShutdownToken;
        _contextFactory = contextFactory ?? ((p, s, sc, ct) => CompositionRoot.BuildContextAsync(p, s, sc, ct));
        _shutdownAction = shutdownAction;
    }

    /// <summary>
    /// Currently-active shell view-model. <c>null</c> after
    /// <see cref="DisposeCurrentAsync"/>. Concrete type is either
    /// <see cref="MainViewModel"/> (loaded context) or
    /// <see cref="EmptyContextViewModel"/> (cold-launch fallback when
    /// args fail but at least one recent is persisted).
    /// </summary>
    public IShellViewModel? Current => _current;

    /// <summary>Per-context scope owning the currently-active view-model. Exposed for tests.</summary>
    public ContextScope? CurrentScope => _currentScope;

    /// <summary>
    /// True while <see cref="SwitchContextAsync"/> is in flight. Bound to the
    /// dropdown's <c>IsEnabled</c> in Phase 7 so the user can't kick off a
    /// second switch on top of an in-flight one.
    /// </summary>
    public bool IsSwitching
    {
        get => _isSwitching;
        private set => SetProperty(ref _isSwitching, value);
    }

    /// <summary>Raised after <see cref="Current"/> changes (build, swap, dispose).</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Cold-launch entry. Parses args, builds the initial context, sets
    /// <see cref="Current"/>. Returns <c>true</c> on success (caller can
    /// show the window) — including when the cold-launch falls back to
    /// the empty-state dropdown picker. Returns <c>false</c> only when
    /// the coordinator has shown the error dialog AND requested
    /// shutdown (no recents present).
    /// </summary>
    public async Task<bool> InitialLaunchAsync(
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parseResult = CompositionRoot.BuildArgs(args);
        if (!parseResult.IsSuccess)
        {
            return HandleColdLaunchFailure(parseResult.Error?.Message ?? "Failed to parse command line.");
        }
        return await StartFromParsedAsync(parseResult.Parsed!, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Cold-launch from an already-parsed command line. Used directly by
    /// tests; <see cref="InitialLaunchAsync"/> wraps it.
    /// </summary>
    public async Task<bool> StartFromParsedAsync(
        ParsedCommandLine parsed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var newScope = new ContextScope(_appShutdownToken);
        MainViewModel newVm;
        try
        {
            newVm = await _contextFactory(parsed, _services, newScope, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await newScope.DisposeAsync().ConfigureAwait(true);
            return HandleColdLaunchFailure(
                ex is ContextBuildException ? ex.Message : $"Failed to start: {ex.Message}");
        }

        _current = newVm;
        _currentScope = newScope;
        OnCurrentChanged();
        await TryRecordAsync(parsed, ct).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Runtime in-place switch. Builds a fresh per-context graph and swaps
    /// it in atomically; the outgoing graph is disposed only AFTER the swap
    /// completes so the window never sees a transient null
    /// <see cref="Current"/>. Concurrent calls serialize via an internal
    /// gate.
    ///
    /// <para>On build failure the outgoing context is left untouched and
    /// the user is offered the chance to remove the failing entry from
    /// recents.</para>
    /// </summary>
    public async Task<bool> SwitchContextAsync(
        ParsedCommandLine parsed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        await _switchGate.WaitAsync(ct).ConfigureAwait(true);
        IsSwitching = true;
        try
        {
            return await SwitchContextCoreAsync(parsed, ct).ConfigureAwait(true);
        }
        finally
        {
            IsSwitching = false;
            _switchGate.Release();
        }
    }

    private async Task<bool> SwitchContextCoreAsync(ParsedCommandLine parsed, CancellationToken ct)
    {
        var newScope = new ContextScope(_appShutdownToken);
        MainViewModel newVm;
        try
        {
            newVm = await _contextFactory(parsed, _services, newScope, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Build (or partial construction) failed — tear down whatever
            // was registered with the half-built scope. The current VM /
            // scope are untouched; the user keeps their existing context.
            await newScope.DisposeAsync().ConfigureAwait(true);

            var msg = ex is ContextBuildException ex2
                ? ex2.Message
                : $"Failed to switch context: {ex.Message}";

            if (_dialog.ConfirmRemoveStaleEntry(parsed.RepoPath, msg))
            {
                try
                {
                    var identity = ContextIdentityFactory.Create(parsed.RepoPath, parsed.Left, parsed.Right);
                    await _services.RecentContextsService.RemoveAsync(identity, ct).ConfigureAwait(true);
                }
                catch
                {
                    // Best-effort: failure to remove a recents entry should
                    // not propagate as a switch failure (the switch already
                    // failed for a different reason).
                }
            }
            return false;
        }

        // Atomic swap on the calling (UI) thread.
        var outgoingVm = _current;
        _current = newVm;
        _currentScope = newScope;
        OnCurrentChanged();

        // Outgoing VM / scope are dropped from this object's state above;
        // dispose them only after the new context is live so listeners see
        // a non-null Current at all times.
        if (outgoingVm is not null)
        {
            await DisposeShellAsync(outgoingVm).ConfigureAwait(true);
        }

        await TryRecordAsync(parsed, ct).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Dispose the currently-active view-model (called from
    /// <c>Window.Closed</c>). Safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeCurrentAsync()
    {
        var outgoing = _current;
        _current = null;
        _currentScope = null;
        OnCurrentChanged();

        if (outgoing is not null)
        {
            await DisposeShellAsync(outgoing).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// <see cref="IContextSwitcher"/> entry point used by the recents
    /// dropdown. Converts a <see cref="RecentLaunchContext"/> into a
    /// <see cref="ParsedCommandLine"/> (using the user's stored display
    /// sides verbatim) and delegates to <see cref="SwitchContextAsync"/>.
    /// </summary>
    public Task<bool> SwitchToRecentAsync(RecentLaunchContext recent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recent);
        var parsed = new ParsedCommandLine(
            recent.Identity.CanonicalRepoPath,
            recent.LeftDisplay,
            recent.RightDisplay);
        return SwitchContextAsync(parsed, ct);
    }

    private async Task TryRecordAsync(ParsedCommandLine parsed, CancellationToken ct)
    {
        try
        {
            var identity = ContextIdentityFactory.Create(parsed.RepoPath, parsed.Left, parsed.Right);
            await _services.RecentContextsService.RecordLaunchAsync(
                identity, parsed.Left, parsed.Right, ct).ConfigureAwait(true);
        }
        catch
        {
            // Recording is best-effort: a launch should not be failed
            // because we couldn't update recents.json.
        }
    }

    /// <summary>
    /// Handle a cold-launch failure. Returns <c>true</c> when the app
    /// can continue (empty-state shell installed, window should show);
    /// <c>false</c> when the user-facing error has been shown and
    /// shutdown has been requested.
    /// </summary>
    private bool HandleColdLaunchFailure(string errorMessage)
    {
        // Cold-launch fallback (DR-007): if at least one recent is
        // persisted, swap in an empty-state shell so the user can pick
        // a recent from the dropdown rather than seeing the app
        // immediately exit.
        if (_services.RecentContextsService.Current.Count > 0)
        {
            var recents = new RecentContextsViewModel(
                _services.RecentContextsService,
                this,
                currentIdentity: null);
            var emptyVm = new EmptyContextViewModel(
                recents,
                $"{errorMessage}{Environment.NewLine}{Environment.NewLine}Pick a recent context above to load it.");

            _current = emptyVm;
            _currentScope = null;
            OnCurrentChanged();
            return true; // window shows with the empty-state dropdown
        }

        _dialog.ShowError("DiffViewer", errorMessage);
        if (_shutdownAction is not null) _shutdownAction(1);
        else Application.Current?.Shutdown(1);
        return false;
    }

    private static async Task DisposeShellAsync(IShellViewModel shell)
    {
        // MainViewModel implements IAsyncDisposable; EmptyContextViewModel
        // is a plain IDisposable. Call the appropriate one and swallow
        // failures (the outgoing graph is unreachable; let it be GC'd).
        try
        {
            switch (shell)
            {
                case IAsyncDisposable async:
                    await async.DisposeAsync().ConfigureAwait(true);
                    break;
                case IDisposable sync:
                    sync.Dispose();
                    break;
            }
        }
        catch { /* best-effort */ }
    }

    private void OnCurrentChanged()
    {
        OnPropertyChanged(nameof(Current));
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
