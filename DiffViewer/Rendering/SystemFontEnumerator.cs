using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DiffViewer.ViewModels;

namespace DiffViewer.Rendering;

/// <summary>
/// Enumerates installed system fonts for the Settings dialog's font
/// dropdown. Monospaced detection uses WPF's <see cref="FormattedText"/>
/// to compare the rendered width of narrow and wide glyphs in each
/// typeface — if "iiii" and "MMMM" render at (essentially) the same
/// width, the font is fixed-pitch.
///
/// <para>The result is cached after the first call because the system
/// font set doesn't change at runtime and the enumeration is a couple
/// hundred FormattedText measurements (≈50–100 ms cold).</para>
/// </summary>
public static class SystemFontEnumerator
{
    private const double ProbeFontSize = 12.0;
    private const double ProbeMaxDelta = 0.5;
    private const double DefaultPixelsPerDip = 1.0;

    private static IReadOnlyList<FontFamilyOption>? _cached;
    private static readonly object _lock = new();

    /// <summary>
    /// Enumerate installed system fonts, sorted with monospaced first,
    /// then variable-width, each block alphabetical (case-insensitive).
    /// Cached after the first call.
    /// </summary>
    public static IReadOnlyList<FontFamilyOption> Enumerate()
    {
        if (_cached is not null) return _cached;
        lock (_lock)
        {
            _cached ??= EnumerateCore();
            return _cached;
        }
    }

    private static IReadOnlyList<FontFamilyOption> EnumerateCore()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FontFamilyOption>();
        foreach (var family in Fonts.SystemFontFamilies)
        {
            var name = family.Source;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!seen.Add(name)) continue;
            list.Add(new FontFamilyOption(name, IsMonospaced(family)));
        }
        return list
            .OrderByDescending(x => x.IsMonospaced)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsMonospaced(FontFamily family)
    {
        try
        {
            var typeface = new Typeface(
                family,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);
            var narrow = MeasureWidth(typeface, "iiii");
            var wide = MeasureWidth(typeface, "MMMM");
            return Math.Abs(narrow - wide) < ProbeMaxDelta;
        }
        catch
        {
            return false;
        }
    }

    private static double MeasureWidth(Typeface tf, string text)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            tf,
            ProbeFontSize,
            Brushes.Black,
            DefaultPixelsPerDip);
        return ft.Width;
    }
}
