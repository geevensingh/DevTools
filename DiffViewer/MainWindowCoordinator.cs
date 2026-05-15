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
public sealed class MainWindowCoordinator : ObservableObject
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

    private MainViewModel? _current;
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

    /// <summary>Currently-active view-model. <c>null</c> after <see cref="DisposeCurrentAsync"/>.</summary>
    public MainViewModel? Current => _current;

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
    /// show the window), <c>false</c> on failure (the dialog has already
    /// been shown and shutdown has been requested).
    /// </summary>
    public async Task<bool> InitialLaunchAsync(
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parseResult = CompositionRoot.BuildArgs(args);
        if (!parseResult.IsSuccess)
        {
            HandleColdLaunchFailure(parseResult.Error?.Message ?? "Failed to parse command line.");
            return false;
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
            HandleColdLaunchFailure(
                ex is ContextBuildException ? ex.Message : $"Failed to start: {ex.Message}");
            return false;
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
            try { await outgoingVm.DisposeAsync().ConfigureAwait(true); }
            catch
            {
                // Disposal failures are best-effort; the outgoing graph is
                // unreachable and will be GC'd.
            }
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
            try { await outgoing.DisposeAsync().ConfigureAwait(true); }
            catch { /* best-effort */ }
        }
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

    private void HandleColdLaunchFailure(string errorMessage)
    {
        // TODO Phase 7: when _services.RecentContextsService.Current.Count > 0
        // and a placeholder VM is available, set Current to it (with a
        // populated dropdown) instead of shutting down. Until the
        // placeholder UI lands, we always shutdown so a stale shortcut
        // surfaces a clean failure rather than a half-rendered window.
        _dialog.ShowError("DiffViewer", errorMessage);
        if (_shutdownAction is not null) _shutdownAction(1);
        else Application.Current?.Shutdown(1);
    }

    private void OnCurrentChanged()
    {
        OnPropertyChanged(nameof(Current));
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
