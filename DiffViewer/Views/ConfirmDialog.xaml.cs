using System.Windows;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmationResult Result { get; private set; } = ConfirmationResult.Cancel();

    public ConfirmDialog(ConfirmationRequest request)
    {
        InitializeComponent();

        Title = request.Title;
        DataContext = new ConfirmDialogViewModel
        {
            Title = request.Title,
            Message = request.Message,
            ConfirmText = request.ConfirmText,
            CancelText = request.CancelText,
            DontAskAgainVisibility = request.ShowDontAskAgain ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        Result = ConfirmationResult.Yes(dontAskAgain: DontAskAgainCheck.IsChecked == true);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = ConfirmationResult.Cancel();
        DialogResult = false;
        Close();
    }

    private sealed class ConfirmDialogViewModel
    {
        public string Title { get; init; } = "";
        public string Message { get; init; } = "";
        public string ConfirmText { get; init; } = "";
        public string CancelText { get; init; } = "";
        public Visibility DontAskAgainVisibility { get; init; }
    }
}
