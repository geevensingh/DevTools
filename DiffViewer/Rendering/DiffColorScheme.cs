using System.Windows.Media;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Five colors that drive every diff highlight in the right pane: line
/// backgrounds for added / removed / modified, plus brighter intra-line
/// spans for added / removed. The settings dialog lets the user pick a
/// preset (Classic, GitHub, High contrast, etc.) or hand-edit a custom
/// palette into <c>settings.json</c>; <see cref="From"/> resolves the
/// stored <see cref="ColorSchemeChoice"/> into a concrete brush set.
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

    /// <summary>GitHub web-diff: subtle pastels, low saturation.</summary>
    public static DiffColorScheme GitHub { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0xDA, 0xFB, 0xE1)),
        RemovedLineBackground = Freeze(Brush(0xFF, 0xEB, 0xE9)),
        ModifiedLineBackground = Freeze(Brush(0xFF, 0xF8, 0xC5)),
        AddedIntraLineBackground = Freeze(Brush(0xAC, 0xEE, 0xBB)),
        RemovedIntraLineBackground = Freeze(Brush(0xFF, 0xC1, 0xC0)),
    };

    /// <summary>Saturated colors for bright displays / low-vision users.</summary>
    public static DiffColorScheme HighContrast { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0x9C, 0xFF, 0x9C)),
        RemovedLineBackground = Freeze(Brush(0xFF, 0x9C, 0x9C)),
        ModifiedLineBackground = Freeze(Brush(0xFF, 0xEB, 0x6B)),
        AddedIntraLineBackground = Freeze(Brush(0x4A, 0xE0, 0x4A)),
        RemovedIntraLineBackground = Freeze(Brush(0xFF, 0x42, 0x42)),
    };

    /// <summary>Blue / orange substitution covering deuteranopia + protanopia.</summary>
    public static DiffColorScheme ColorblindFriendly { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0xCF, 0xE2, 0xF3)),
        RemovedLineBackground = Freeze(Brush(0xFC, 0xE5, 0xCD)),
        ModifiedLineBackground = Freeze(Brush(0xEA, 0xD1, 0xDC)),
        AddedIntraLineBackground = Freeze(Brush(0x6F, 0xA8, 0xDC)),
        RemovedIntraLineBackground = Freeze(Brush(0xF6, 0xB2, 0x6B)),
    };

    /// <summary>Peach / cyan on Solarized cream.</summary>
    public static DiffColorScheme SolarizedLight { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0xEE, 0xE8, 0xD5)),
        RemovedLineBackground = Freeze(Brush(0xF7, 0xD8, 0xC9)),
        ModifiedLineBackground = Freeze(Brush(0xF1, 0xE5, 0xB6)),
        AddedIntraLineBackground = Freeze(Brush(0x93, 0xA1, 0xA1)),
        RemovedIntraLineBackground = Freeze(Brush(0xCB, 0x4B, 0x16)),
    };

    /// <summary>Extra-soft pastels for long reading sessions.</summary>
    public static DiffColorScheme Pale { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0xF0, 0xFA, 0xF2)),
        RemovedLineBackground = Freeze(Brush(0xFD, 0xF4, 0xF4)),
        ModifiedLineBackground = Freeze(Brush(0xFD, 0xFA, 0xEC)),
        AddedIntraLineBackground = Freeze(Brush(0xCD, 0xEB, 0xD2)),
        RemovedIntraLineBackground = Freeze(Brush(0xF4, 0xCE, 0xD0)),
    };

    /// <summary>Grayscale only - useful for printing or screenshots.</summary>
    public static DiffColorScheme Monochrome { get; } = new()
    {
        AddedLineBackground = Freeze(Brush(0xEE, 0xEE, 0xEE)),
        RemovedLineBackground = Freeze(Brush(0xDD, 0xDD, 0xDD)),
        ModifiedLineBackground = Freeze(Brush(0xF5, 0xF5, 0xF5)),
        AddedIntraLineBackground = Freeze(Brush(0xC0, 0xC0, 0xC0)),
        RemovedIntraLineBackground = Freeze(Brush(0xA0, 0xA0, 0xA0)),
    };

    /// <summary>
    /// Resolve a stored <see cref="ColorSchemeChoice"/> from settings into
    /// a concrete brush set. Unknown preset names fall back to <see cref="Classic"/>;
    /// custom palettes parse <c>#RRGGBB</c> / <c>#AARRGGBB</c> hex strings via
    /// <see cref="ColorConverter"/> and fall back per-color to Classic on parse failure.
    /// </summary>
    public static DiffColorScheme From(ColorSchemeChoice choice) => choice switch
    {
        ColorSchemeChoice.PresetScheme p => FromPreset(p.Name),
        ColorSchemeChoice.CustomScheme c => FromCustom(c.Colors),
        _ => Classic,
    };

    private static DiffColorScheme FromPreset(ColorSchemePresetName name) => name switch
    {
        ColorSchemePresetName.Classic => Classic,
        ColorSchemePresetName.GitHub => GitHub,
        ColorSchemePresetName.HighContrast => HighContrast,
        ColorSchemePresetName.ColorblindFriendly => ColorblindFriendly,
        ColorSchemePresetName.SolarizedLight => SolarizedLight,
        ColorSchemePresetName.Pale => Pale,
        ColorSchemePresetName.Monochrome => Monochrome,
        _ => Classic,
    };

    private static DiffColorScheme FromCustom(ColorSchemeColors c) => new()
    {
        AddedLineBackground = ParseOrDefault(c.AddedLineBg, Classic.AddedLineBackground),
        RemovedLineBackground = ParseOrDefault(c.RemovedLineBg, Classic.RemovedLineBackground),
        ModifiedLineBackground = ParseOrDefault(c.ModifiedLineBg, Classic.ModifiedLineBackground),
        AddedIntraLineBackground = ParseOrDefault(c.AddedIntraline, Classic.AddedIntraLineBackground),
        RemovedIntraLineBackground = ParseOrDefault(c.RemovedIntraline, Classic.RemovedIntraLineBackground),
    };

    private static Brush ParseOrDefault(string hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
                return Freeze(new SolidColorBrush(c));
        }
        catch (FormatException) { /* fall through */ }
        return fallback;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
