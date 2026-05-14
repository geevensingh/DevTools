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
            var shape = ClassifyHunk(hunk);

            // Gate each column by the hunk's true shape. Old/NewLineCount
            // include context lines (see DiffHunk XML docs), so a pure-delete
            // hunk's NewLineCount is non-zero whenever it has surrounding
            // context — which would otherwise paint a phantom "added"
            // column on the right side. Same story for pure-inserts on the
            // left. Mixed hunks keep both rects.
            Rect? left = shape == HunkChangeShape.PureInsert
                ? null
                : ComputeColumnRect(
                    hunk.OldStartLine, hunk.OldLineCount, leftTotalLines, barHeight, x: 0, w: cw);
            Rect? right = shape == HunkChangeShape.PureDelete
                ? null
                : ComputeColumnRect(
                    hunk.NewStartLine, hunk.NewLineCount, rightTotalLines, barHeight, x: rightColumnLeftX, w: cw);
            result.Add(new HunkBarLayout(i, left, right, shape));
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
    private static bool ContainsPoint(HunkBarLayout layout, Point p) =>
        ContainsInRectsOrRibbon(layout.LeftRect, layout.RightRect, p);

    /// <summary>
    /// Shared containment test for a (LeftRect, RightRect) pair: a point
    /// hits if it falls inside either rect or inside the trapezoid ribbon
    /// linearly interpolated between them. Used by both hunk hit-testing
    /// and viewport-band hit-testing — the geometry is identical.
    /// </summary>
    private static bool ContainsInRectsOrRibbon(Rect? leftRect, Rect? rightRect, Point p)
    {
        if (leftRect is Rect L && L.Contains(p)) return true;
        if (rightRect is Rect R && R.Contains(p)) return true;

        // Trapezoid ribbon: only exists when both sides are present.
        if (leftRect is Rect lr && rightRect is Rect rr)
        {
            double xL = lr.Right;
            double xR = rr.Left;
            if (p.X < xL || p.X > xR) return false;
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
    /// Per-side band rectangles for the editor's currently-visible window,
    /// painted by <see cref="HunkOverviewBar"/> as a soft fill plus crisp
    /// outline. Either rect can be <c>null</c> if that side has no presence
    /// in the visible window (e.g. inline mode where the window contains
    /// only pure-insert lines — the old side has no representation).
    /// </summary>
    public sealed record ViewportBand(Rect? LeftRect, Rect? RightRect);

    /// <summary>
    /// Build the geometry for the viewport indicator from
    /// <paramref name="state"/>. Returns <c>null</c> when the state is
    /// itself <c>null</c> or every side resolves to an empty rect.
    ///
    /// <para>Uses the same line-fraction mapping as
    /// <see cref="ComputeColumnRect"/> but skips the
    /// <see cref="MinHitHeight"/> inflation — the viewport band is
    /// typically tall enough that inflation only adds visual noise.
    /// When the visible window is taller than the underlying file the
    /// band is clamped to the bar's height.</para>
    /// </summary>
    public static ViewportBand? ComputeViewport(
        ViewportState? state,
        int leftTotalLines,
        int rightTotalLines,
        double barWidth,
        double barHeight,
        double columnWidth)
    {
        if (state is null || barWidth <= 0 || barHeight <= 0) return null;

        double maxColumnWidth = Math.Max(1.0, (barWidth - 1.0) / 2.0);
        double cw = Math.Min(columnWidth, maxColumnWidth);
        double rightColumnLeftX = barWidth - cw;

        Rect? left = ComputeBandRect(
            state.LeftFirstLine, state.LeftLastLine, leftTotalLines, barHeight, x: 0, w: cw);
        Rect? right = ComputeBandRect(
            state.RightFirstLine, state.RightLastLine, rightTotalLines, barHeight, x: rightColumnLeftX, w: cw);

        if (left is null && right is null) return null;
        return new ViewportBand(left, right);
    }

    /// <summary>
    /// Position one side's viewport-band rect. <c>0</c> for either line
    /// number is a sentinel for "this side is unrepresented in the visible
    /// window" (only happens in inline mode); returns <c>null</c> in that
    /// case so the trapezoid degenerates to a single column.
    /// </summary>
    private static Rect? ComputeBandRect(
        int firstLine, int lastLine, int totalLines, double barHeight, double x, double w)
    {
        if (firstLine <= 0 || lastLine <= 0) return null;
        if (totalLines <= 0 || barHeight <= 0) return null;
        if (lastLine < firstLine) lastLine = firstLine;
        if (firstLine > totalLines) firstLine = totalLines;
        if (lastLine > totalLines) lastLine = totalLines;

        double startFrac = (double)(firstLine - 1) / totalLines;
        double endFrac = Math.Min(1.0, (double)lastLine / totalLines);
        if (startFrac < 0) startFrac = 0;
        if (startFrac > 1) startFrac = 1;

        double top = startFrac * barHeight;
        double bottom = endFrac * barHeight;
        double h = Math.Max(0, bottom - top);

        // Viewport larger than file → clamp to bar height.
        if (top + h > barHeight) h = barHeight - top;
        if (h <= 0) return null;

        return new Rect(x, top, w, h);
    }

    /// <summary>
    /// Does <paramref name="p"/> fall inside the viewport band? Same
    /// rect-or-ribbon geometry as the hunk hit-test.
    /// </summary>
    public static bool IsInsideBand(ViewportBand band, Point p) =>
        ContainsInRectsOrRibbon(band.LeftRect, band.RightRect, p);

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
