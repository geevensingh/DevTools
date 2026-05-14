using System.Windows;
using System.Windows.Media;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Pure geometry / hit-test helpers for <see cref="HunkOverviewBar"/>.
/// Split out so the marker-positioning and click-resolution logic can be
/// unit-tested without spinning up a WPF visual tree.
/// </summary>
internal static class HunkOverviewBarGeometry
{
    /// <summary>Minimum pixel height for any single-side hunk rect.</summary>
    public const double MinHitHeight = 4.0;

    /// <summary>
    /// Per-hunk geometry for the two-column overview bar. <see cref="LeftRect"/>
    /// is null for pure-insert hunks (nothing was deleted from the old file);
    /// <see cref="RightRect"/> is null for pure-delete hunks. When both are
    /// non-null, the bar draws a trapezoid ribbon between them.
    /// </summary>
    public sealed record HunkBarLayout(
        int HunkIndex,
        Rect? LeftRect,
        Rect? RightRect,
        HunkChangeShape Shape);

    /// <summary>
    /// Build the layout for every hunk against a bar
    /// <paramref name="barWidth"/> × <paramref name="barHeight"/> pixels in
    /// size. The left column (deletions) is anchored to x=0, the right
    /// column (insertions) is anchored to x=barWidth-columnWidth, and the
    /// gap between is reserved for the ribbon connecting paired markers.
    /// Each column scales independently to its file's line count so the
    /// two columns visualise relative file sizes.
    /// </summary>
    public static IReadOnlyList<HunkBarLayout> ComputeLayouts(
        IReadOnlyList<DiffHunk> hunks,
        int leftTotalLines,
        int rightTotalLines,
        double barWidth,
        double barHeight,
        double columnWidth)
    {
        if (hunks.Count == 0 || barWidth <= 0 || barHeight <= 0)
        {
            return Array.Empty<HunkBarLayout>();
        }

        // Clamp the column width so two columns plus at least a 1 px ribbon
        // always fit.
        double maxColumnWidth = Math.Max(1.0, (barWidth - 1.0) / 2.0);
        double cw = Math.Min(columnWidth, maxColumnWidth);

        double rightColumnLeftX = barWidth - cw;

        var result = new List<HunkBarLayout>(hunks.Count);
        for (int i = 0; i < hunks.Count; i++)
        {
            var hunk = hunks[i];
            Rect? left = ComputeColumnRect(
                hunk.OldStartLine, hunk.OldLineCount, leftTotalLines, barHeight, x: 0, w: cw);
            Rect? right = ComputeColumnRect(
                hunk.NewStartLine, hunk.NewLineCount, rightTotalLines, barHeight, x: rightColumnLeftX, w: cw);
            result.Add(new HunkBarLayout(i, left, right, ClassifyHunk(hunk)));
        }
        return result;
    }

    /// <summary>
    /// Position one column's rect for a hunk on a particular side. Returns
    /// null when the hunk has no presence on this side (e.g. left-side
    /// rect for a pure-insert), letting the renderer skip drawing.
    /// </summary>
    private static Rect? ComputeColumnRect(
        int startLine, int lineCount, int totalLines, double barHeight, double x, double w)
    {
        if (lineCount <= 0) return null;
        if (totalLines <= 0 || barHeight <= 0) return null;

        // git's @@ headers use startLine=0 for empty-side hunks; we already
        // bail out above when there's no content. But also guard the math.
        if (startLine < 1) startLine = 1;

        double startFrac = (double)(startLine - 1) / totalLines;
        double endFrac = Math.Min(1.0, (double)(startLine - 1 + lineCount) / totalLines);
        if (startFrac < 0) startFrac = 0;
        if (startFrac > 1) startFrac = 1;

        double naturalTop = startFrac * barHeight;
        double naturalBottom = endFrac * barHeight;
        double naturalH = Math.Max(0, naturalBottom - naturalTop);

        if (naturalH < MinHitHeight)
        {
            // Inflate sub-pixel rects to a clickable target around their
            // natural midpoint, then nudge inside the bar if we'd run off
            // either edge.
            double mid = (naturalTop + naturalBottom) / 2.0;
            naturalTop = Math.Max(0, mid - MinHitHeight / 2.0);
            naturalH = MinHitHeight;
            if (naturalTop + naturalH > barHeight)
                naturalTop = Math.Max(0, barHeight - naturalH);
        }

        return new Rect(x, naturalTop, w, naturalH);
    }

    /// <summary>
    /// Map a click position to a hunk index. A click counts as hitting a
    /// hunk if it falls inside either the left rect, the right rect, or the
    /// trapezoid ribbon between them (for mixed hunks). When two markers
    /// overlap (both inflated to <see cref="MinHitHeight"/> in a tall file)
    /// we prefer whichever has its midpoint closer to the click.
    /// </summary>
    public static int HitTest(IReadOnlyList<HunkBarLayout> layouts, Point p)
    {
        if (layouts.Count == 0) return -1;
        int bestIdx = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            if (!ContainsPoint(layout, p)) continue;

            double midY = MidY(layout);
            double dist = Math.Abs(p.Y - midY);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Does the click fall inside the layout's interactable region? Region
    /// is the union of (a) the left rect, (b) the right rect, (c) the
    /// trapezoid ribbon that linearly interpolates between the two rects'
    /// vertical extents along the bar's horizontal axis.
    /// </summary>
    private static bool ContainsPoint(HunkBarLayout layout, Point p)
    {
        if (layout.LeftRect is Rect L && L.Contains(p)) return true;
        if (layout.RightRect is Rect R && R.Contains(p)) return true;

        // Trapezoid ribbon: only exists when both sides are present.
        if (layout.LeftRect is Rect lr && layout.RightRect is Rect rr)
        {
            double xL = lr.Right;
            double xR = rr.Left;
            if (p.X < xL || p.X > xR) return false;
            // Linear interpolation factor from left edge of ribbon to right.
            double span = xR - xL;
            double t = span <= 0 ? 0 : (p.X - xL) / span;
            double topAtX = Lerp(lr.Top, rr.Top, t);
            double bottomAtX = Lerp(lr.Bottom, rr.Bottom, t);
            return p.Y >= topAtX && p.Y <= bottomAtX;
        }
        return false;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double MidY(HunkBarLayout layout)
    {
        // Midpoint of whichever side(s) are present; for mixed hunks it's
        // the midpoint of the combined Y-range so overlap resolution picks
        // a sensible representative.
        double? lTop = layout.LeftRect?.Top;
        double? lBot = layout.LeftRect?.Bottom;
        double? rTop = layout.RightRect?.Top;
        double? rBot = layout.RightRect?.Bottom;
        double top = Math.Min(lTop ?? double.MaxValue, rTop ?? double.MaxValue);
        double bot = Math.Max(lBot ?? double.MinValue, rBot ?? double.MinValue);
        return (top + bot) / 2.0;
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
