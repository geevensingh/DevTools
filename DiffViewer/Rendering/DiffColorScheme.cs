using System.Windows.Media;

namespace DiffViewer.Rendering;

/// <summary>
/// Five colors that drive every diff highlight in the right pane: line
/// backgrounds for added / removed / modified, plus brighter intra-line
/// spans for added / removed. The settings dialog will eventually let the
/// user pick a preset (Classic, GitHub, High contrast, etc.); for now only
/// <see cref="Classic"/> is wired up.
/// </summary>
public sealed class DiffColorScheme
{
    public required Brush AddedLineBackground { get; init; }
    public required Brush RemovedLineBackground { get; init; }
    public required Brush ModifiedLineBackground { get; init; }
    public required Brush AddedIntraLineBackground { get; init; }
    public required Brush RemovedIntraLineBackground { get; init; }

    /// <summary>
    /// The default preset - pale red / green / yellow line tints with
    /// brighter intra-line spans, matching git's CLI tradition.
    /// </summary>
    public static DiffColorScheme Classic { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0xE6, 0xFF, 0xEC)),
        RemovedLineBackground = Freeze(Brush(0xFF, 0xEE, 0xF0)),
        ModifiedLineBackground = Freeze(Brush(0xFF, 0xF5, 0xD0)),
        AddedIntraLineBackground = Freeze(Brush(0xAC, 0xEE, 0xBB)),
        RemovedIntraLineBackground = Freeze(Brush(0xFD, 0xB8, 0xC0)),
    };

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
