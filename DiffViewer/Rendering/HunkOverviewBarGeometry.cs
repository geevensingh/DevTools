using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Pure geometry / hit-test helpers for <see cref="HunkOverviewBar"/>.
/// Split out so the marker-positioning and click-resolution logic can be
/// unit-tested without spinning up a WPF visual tree.
/// </summary>
internal static class HunkOverviewBarGeometry
{
    /// <summary>Minimum pixel height for any single hunk's hit-test rect.</summary>
    public const double MinHitHeight = 4.0;

    /// <summary>
    /// Returns the y-offset and height (px) for the marker representing the
    /// supplied hunk against a bar of <paramref name="barHeight"/> pixels.
    /// The natural rect is proportional to the hunk's line count; if the
    /// result would be shorter than <see cref="MinHitHeight"/> it gets
    /// inflated, keeping the natural midpoint stable and clamping the
    /// result inside the bar.
    /// </summary>
    public static (double Y, double Height) ComputeMarkerRect(
        DiffHunk hunk, int totalLines, double barHeight)
    {
        if (totalLines <= 0 || barHeight <= 0) return (0, 0);

        int startLine = hunk.NewStartLine > 0 ? hunk.NewStartLine : Math.Max(1, hunk.OldStartLine);
        int lineCount = Math.Max(hunk.NewLineCount, hunk.OldLineCount);
        if (lineCount < 1) lineCount = 1;

        double startFrac = (double)(startLine - 1) / totalLines;
        double endFrac = Math.Min(1.0, (double)(startLine - 1 + lineCount) / totalLines);
        if (startFrac < 0) startFrac = 0;
        if (startFrac > 1) startFrac = 1;

        double naturalTop = startFrac * barHeight;
        double naturalBottom = endFrac * barHeight;
        double naturalH = Math.Max(0, naturalBottom - naturalTop);
        if (naturalH < MinHitHeight)
        {
            double mid = (naturalTop + naturalBottom) / 2.0;
            naturalTop = Math.Max(0, mid - MinHitHeight / 2.0);
            naturalH = MinHitHeight;
            if (naturalTop + naturalH > barHeight)
                naturalTop = Math.Max(0, barHeight - naturalH);
        }
        return (naturalTop, naturalH);
    }

    /// <summary>
    /// Map a y-coordinate to a hunk index, returning <c>-1</c> if no marker
    /// covers it. When two markers overlap (e.g. two hunks both expanded to
    /// the 4 px minimum), picks the one whose vertical midpoint is closer
    /// to <paramref name="y"/>.
    /// </summary>
    public static int HitTest(
        IReadOnlyList<DiffHunk> hunks, int totalLines, double barHeight, double y)
    {
        if (hunks.Count == 0 || totalLines <= 0 || barHeight <= 0) return -1;
        int bestIdx = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < hunks.Count; i++)
        {
            var (top, h) = ComputeMarkerRect(hunks[i], totalLines, barHeight);
            double bottom = top + h;
            if (y < top || y > bottom) continue;
            double dist = Math.Abs(y - (top + h / 2.0));
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Classifies a hunk's content for color selection — pure-add (all
    /// inserted lines), pure-delete (all removed lines), or mixed.
    /// </summary>
    public static HunkChangeShape ClassifyHunk(DiffHunk hunk)
    {
        bool hasAdds = false, hasDels = false;
        foreach (var line in hunk.Lines)
        {
            if (line.Kind == DiffLineKind.Inserted) hasAdds = true;
            else if (line.Kind == DiffLineKind.Deleted) hasDels = true;
            if (hasAdds && hasDels) break;
        }
        return (hasAdds, hasDels) switch
        {
            (true, false) => HunkChangeShape.PureInsert,
            (false, true) => HunkChangeShape.PureDelete,
            _ => HunkChangeShape.Mixed,
        };
    }
}

internal enum HunkChangeShape
{
    PureInsert,
    PureDelete,
    Mixed,
}
