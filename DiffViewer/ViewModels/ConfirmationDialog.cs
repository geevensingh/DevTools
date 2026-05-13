namespace DiffViewer.ViewModels;

/// <summary>
/// Inputs to a <see cref="MainViewModel.ConfirmHandler"/> invocation. The
/// View renders these as a modal dialog (typically a confirmation prompt
/// for a destructive operation).
/// </summary>
/// <param name="Title">Window title (one short line).</param>
/// <param name="Message">Body text. May contain newlines for paragraphs.</param>
/// <param name="ConfirmText">Affirmative button label (e.g. "Delete", "Revert").</param>
/// <param name="CancelText">Cancel button label.</param>
/// <param name="ShowDontAskAgain">If true, the dialog renders a "Don't ask me again" checkbox whose state is reported on <see cref="ConfirmationResult.DontAskAgain"/>.</param>
public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText,
    bool ShowDontAskAgain);

/// <summary>Result of a <see cref="ConfirmationRequest"/>.</summary>
/// <param name="Confirmed"><c>true</c> if the user clicked the confirm button; <c>false</c> on cancel / dismiss.</param>
/// <param name="DontAskAgain"><c>true</c> if the user ticked the "Don't ask me again" checkbox; ignored when <see cref="ConfirmationRequest.ShowDontAskAgain"/> was false.</param>
public sealed record ConfirmationResult(bool Confirmed, bool DontAskAgain)
{
    public static ConfirmationResult Cancel() => new(false, false);
    public static ConfirmationResult Yes(bool dontAskAgain = false) => new(true, dontAskAgain);
}
