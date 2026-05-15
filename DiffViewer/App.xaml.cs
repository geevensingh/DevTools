using System;
using System.Threading;
using System.Windows;
using DiffViewer.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;

namespace DiffViewer;

public partial class App : Application
{
    private CancellationTokenSource? _shutdownCts;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _shutdownCts = new CancellationTokenSource();

        // Construct App-level singletons that survive in-place context
        // switches. Per-context resources (RepositoryService etc.) are
        // built inside CompositionRoot.BuildContextAsync and registered
        // with the per-VM ContextScope.
        var settingsService = new SettingsService();
        var diffService = new DiffService();
        var externalAppLauncher = new ExternalAppLauncher(settingsService);
        IRecentContextsService recents = new NullRecentContextsService();
        var services = new AppServices(settingsService, diffService, externalAppLauncher, recents);

        var parseResult = CompositionRoot.BuildArgs(e.Args);
        if (!parseResult.IsSuccess)
        {
            MessageBox.Show(
                parseResult.Error?.Message ?? "DiffViewer failed to start.",
                "DiffViewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var scope = new ContextScope(_shutdownCts.Token);
        MainViewModel vm;
        try
        {
            vm = await CompositionRoot.BuildContextAsync(
                parseResult.Parsed!, services, scope, _shutdownCts.Token);
        }
        catch (Exception ex)
        {
            // Tear down any partially-constructed per-context graph.
            await scope.DisposeAsync();
            MessageBox.Show(
                ex is ContextBuildException ? ex.Message : $"DiffViewer failed to start: {ex.Message}",
                "DiffViewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = vm };
        window.Closed += async (_, _) =>
        {
            await vm.DisposeAsync();
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _shutdownCts?.Cancel(); } catch { }
        try { _shutdownCts?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
