namespace DiffViewer.ViewModels;

/// <summary>
/// One entry in the Settings dialog's font-family dropdown. The
/// dialog renders the <see cref="Name"/> in its own typeface and
/// groups items by <see cref="GroupName"/> (Monospaced first, then
/// variable-width).
/// </summary>
public sealed record FontFamilyOption(string Name, bool IsMonospaced)
{
    public string GroupName => IsMonospaced ? "Monospaced" : "Variable width";
}
