using System.Windows;

namespace DiffViewer.Services;

/// <summary>
/// Production implementation of <see cref="IDialogService"/> that calls
/// straight into <see cref="MessageBox"/>.
/// </summary>
public sealed class MessageBoxDialogService : IDialogService
{
    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool ConfirmRemoveStaleEntry(string repoPath, string error)
    {
        var msg =
            $"Could not switch to '{repoPath}':\n\n" +
            $"{error}\n\n" +
            "Remove this entry from recents?";
        var result = MessageBox.Show(
            msg,
            "DiffViewer — Switch failed",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        return result == MessageBoxResult.Yes;
    }
}
