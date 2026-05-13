using System.Windows;

namespace DiffViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var vm = CompositionRoot.BuildMainViewModel(e.Args, out var error);
        if (vm is null)
        {
            MessageBox.Show(
                error ?? "DiffViewer failed to start.",
                "DiffViewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = vm };
        window.Closed += (_, _) => vm.Dispose();
        window.Show();
    }
}

