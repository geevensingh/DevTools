using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Modal Settings dialog. View concerns only: routes focus-loss /
/// Enter on numeric and text inputs to <see cref="SettingsViewModel
/// .CommitNumericFields"/>, and flushes the color-scheme debounce on
/// Close.
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnTextOrNumericLostFocus(object sender, RoutedEventArgs e)
    {
        (DataContext as SettingsViewModel)?.CommitNumericFields();
    }

    private void OnTextOrNumericKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            // Push the binding so the VM sees the latest text, then commit.
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            (DataContext as SettingsViewModel)?.CommitNumericFields();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CommitNumericFields();
            vm.FlushPendingWrites();
        }
        Close();
    }
}
