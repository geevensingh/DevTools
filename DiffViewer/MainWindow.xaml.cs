using System.Windows;
using DiffViewer.ViewModels;
using DiffViewer.Views;

namespace DiffViewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            vm.ShowSettingsHandler = ShowSettingsDialog;
            vm.ConfirmHandler = ShowConfirmDialog;
            vm.ToastHandler = ShowToast;
        }
    }

    private ConfirmationResult ShowConfirmDialog(ConfirmationRequest request)
    {
        var dialog = new ConfirmDialog(request) { Owner = this };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void ShowToast(string message)
    {
        // v1: simple status-line surface via the title bar; richer toast UX
        // is in the polish phase. Always marshal to the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            var prevTitle = Title;
            Title = $"DiffViewer — {message}";
            // Best-effort restore after 4s; not exact, but good enough
            // for a v1 status line.
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(4),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (Title.StartsWith("DiffViewer — " + message))
                {
                    Title = prevTitle;
                }
            };
            timer.Start();
        });
    }

    private void ShowSettingsDialog()
    {
        if (DataContext is not MainViewModel vm || vm.SettingsService is null) return;

        var dialogVm = new SettingsViewModel(
            vm.SettingsService,
            confirmReset: prompt => MessageBox.Show(
                this, prompt, "Reset settings",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK);

        var dialog = new SettingsDialog(dialogVm) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialogVm.Dispose(); }
    }
}
