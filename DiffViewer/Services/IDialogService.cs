namespace DiffViewer.Services;

/// <summary>
/// Thin abstraction over the few WPF <c>MessageBox</c> calls the
/// <see cref="DiffViewer.MainWindowCoordinator"/> needs. Exists so
/// coordinator tests can substitute a fake without standing up the WPF
/// dispatcher.
/// </summary>
public interface IDialogService
{
    /// <summary>Show an error message. Title is typically the app name.</summary>
    void ShowError(string title, string message);

    /// <summary>
    /// Ask the user whether a stale recents entry should be removed after a
    /// failed switch. Returns <c>true</c> on Yes (default), <c>false</c> on
    /// No or dismissal.
    /// </summary>
    bool ConfirmRemoveStaleEntry(string repoPath, string error);
}
