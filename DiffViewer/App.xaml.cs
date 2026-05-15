using System;
using System.Threading;
using System.Windows;
using DiffViewer.Services;

namespace DiffViewer;

public partial class App : Application
{
    private CancellationTokenSource? _shutdownCts;
    private MainWindowCoordinator? _coordinator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _shutdownCts = new CancellationTokenSource();

        // App-level singletons that survive in-place context switches.
        // Per-context resources (RepositoryService, watcher, MainViewModel,
        // etc.) are owned by the per-context ContextScope built inside
        // MainWindowCoordinator → CompositionRoot.
        var settingsService = new SettingsService();
        var diffService = new DiffService();
        var externalAppLauncher = new ExternalAppLauncher(settingsService);
        IRecentContextsService recents = new NullRecentContextsService();
        var services = new AppServices(settingsService, diffService, externalAppLauncher, recents);

        _coordinator = new MainWindowCoordinator(
            services,
            new MessageBoxDialogService(),
            _shutdownCts.Token);

        var window = new MainWindow();
        _coordinator.CurrentChanged += (_, _) => window.DataContext = _coordinator.Current;
        window.Closed += async (_, _) =>
        {
            if (_coordinator is not null) await _coordinator.DisposeCurrentAsync();
        };

        var ok = await _coordinator.InitialLaunchAsync(e.Args, _shutdownCts.Token);
        if (!ok)
        {
            // Coordinator already showed the error dialog and called
            // Shutdown(1); just bail out before Show().
            return;
        }

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _shutdownCts?.Cancel(); } catch { }
        try { _shutdownCts?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
