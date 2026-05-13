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
        }
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
