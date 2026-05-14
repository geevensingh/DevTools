using System.Windows;
using DiffViewer.Models;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class HunkOverviewBarGeometryTests
{
    private const double BarWidth = 32.0;
    private const double ColumnWidth = 10.0;

    private static DiffHunk Hunk(
        int newStart,
        int newCount,
        int oldStart = 0,
        int oldCount = 0,
        params DiffLineKind[] kinds)
    {
        var lines = new List<DiffLine>();
        foreach (var k in kinds)
        {
            lines.Add(new DiffLine(k, null, null, ""));
        }
        return new DiffHunk(
            OldStartLine: oldStart,
            OldLineCount: oldCount,
            NewStartLine: newStart,
            NewLineCount: newCount,
            Lines: lines,
            FunctionContext: null);
    }

    private static IReadOnlyList<HunkOverviewBarGeometry.HunkBarLayout> Layouts(
        IReadOnlyList<DiffHunk> hunks,
        int leftTotalLines,
        int rightTotalLines,
        double barHeight,
        double barWidth = BarWidth,
        double columnWidth = ColumnWidth) =>
        HunkOverviewBarGeometry.ComputeLayouts(
            hunks, leftTotalLines, rightTotalLines, barWidth, barHeight, columnWidth);

    // ---------- ComputeLayouts ----------

    [Fact]
    public void ComputeLayouts_MixedHunk_HasBothRectsAndIsRibbonEligible()
    {
        var hunk = Hunk(newStart: 50, newCount: 10, oldStart: 50, oldCount: 10,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts.Should().HaveCount(1);
        layouts[0].Shape.Should().Be(HunkChangeShape.Mixed);
        layouts[0].LeftRect.Should().NotBeNull();
        layouts[0].RightRect.Should().NotBeNull();
    }

    [Fact]
    public void ComputeLayouts_PureAdd_OmitsLeftRect()
    {
        // Pure-insert: the file existed but this hunk's old-side is empty.
        var hunk = Hunk(newStart: 50, newCount: 5, oldStart: 0, oldCount: 0,
            kinds: new[] { DiffLineKind.Inserted, DiffLineKind.Inserted });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].Shape.Should().Be(HunkChangeShape.PureInsert);
        layouts[0].LeftRect.Should().BeNull();
        layouts[0].RightRect.Should().NotBeNull();
    }

    [Fact]
    public void ComputeLayouts_PureDelete_OmitsRightRect()
    {
        var hunk = Hunk(newStart: 0, newCount: 0, oldStart: 50, oldCount: 5,
            kinds: new[] { DiffLineKind.Deleted, DiffLineKind.Deleted });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].Shape.Should().Be(HunkChangeShape.PureDelete);
        layouts[0].LeftRect.Should().NotBeNull();
        layouts[0].RightRect.Should().BeNull();
    }

    [Fact]
    public void ComputeLayouts_PureDeleteHunkWithContext_OmitsRightRect()
    {
        // Regression: DiffService inflates Old/NewLineCount with context
        // lines (default 3 above/below), so a pure-delete hunk reports a
        // non-zero NewLineCount. Without consulting Shape the renderer
        // would paint a phantom green column on the right side.
        var hunk = Hunk(newStart: 50, newCount: 6, oldStart: 50, oldCount: 7,
            kinds: new[]
            {
                DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
                DiffLineKind.Deleted,
                DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
            });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].Shape.Should().Be(HunkChangeShape.PureDelete);
        layouts[0].LeftRect.Should().NotBeNull();
        layouts[0].RightRect.Should().BeNull();
    }

    [Fact]
    public void ComputeLayouts_PureInsertHunkWithContext_OmitsLeftRect()
    {
        // Symmetric regression: pure-insert hunk with surrounding context
        // would otherwise paint a phantom red column on the left side.
        var hunk = Hunk(newStart: 50, newCount: 7, oldStart: 50, oldCount: 6,
            kinds: new[]
            {
                DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
                DiffLineKind.Inserted,
                DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
            });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].Shape.Should().Be(HunkChangeShape.PureInsert);
        layouts[0].LeftRect.Should().BeNull();
        layouts[0].RightRect.Should().NotBeNull();
    }

    [Fact]
    public void ComputeLayouts_ModifiedOnlyHunk_StaysMixed()
    {
        // ClassifyHunk doesn't check Modified — only Inserted/Deleted. A
        // hunk that's nothing but Modified lines must therefore stay Mixed
        // (both columns drawn). This locks down the fallthrough behavior.
        var hunk = Hunk(newStart: 50, newCount: 5, oldStart: 50, oldCount: 5,
            kinds: new[]
            {
                DiffLineKind.Modified, DiffLineKind.Modified, DiffLineKind.Modified,
            });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].Shape.Should().Be(HunkChangeShape.Mixed);
        layouts[0].LeftRect.Should().NotBeNull();
        layouts[0].RightRect.Should().NotBeNull();
    }

    [Fact]
    public void ComputeLayouts_RectsAreAnchoredToTheirOwnColumns()
    {
        // Left rect must hug x=0; right rect must hug barWidth-columnWidth.
        var hunk = Hunk(newStart: 50, newCount: 10, oldStart: 50, oldCount: 10,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].LeftRect!.Value.Left.Should().Be(0);
        layouts[0].LeftRect!.Value.Width.Should().Be(ColumnWidth);
        layouts[0].RightRect!.Value.Right.Should().BeApproximately(BarWidth, 0.01);
        layouts[0].RightRect!.Value.Width.Should().Be(ColumnWidth);
    }

    [Fact]
    public void ComputeLayouts_PositionsProportionallyToOwnFile()
    {
        // 10-line hunk at line 50 in a 100-line file, on a 200-px bar:
        // top at (49/100)*200 = 98, height = 20.
        var hunk = Hunk(newStart: 50, newCount: 10, oldStart: 50, oldCount: 10,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 200);

        layouts[0].RightRect!.Value.Top.Should().BeApproximately(98.0, 0.01);
        layouts[0].RightRect!.Value.Height.Should().BeApproximately(20.0, 0.01);
        layouts[0].LeftRect!.Value.Top.Should().BeApproximately(98.0, 0.01);
    }

    [Fact]
    public void ComputeLayouts_ScalesEachColumnIndependentlyToItsOwnFile()
    {
        // Same hunk lines, but left file is 2x as long as right file: the
        // left rect must be half the height of the right rect.
        var hunk = Hunk(newStart: 5, newCount: 10, oldStart: 5, oldCount: 10,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 200, rightTotalLines: 100, barHeight: 100);

        layouts[0].LeftRect!.Value.Height
            .Should().BeApproximately(layouts[0].RightRect!.Value.Height / 2.0, 0.01);
    }

    [Fact]
    public void ComputeLayouts_InflatesShortRectsToMinHitHeight()
    {
        // 1-line hunk in a 1000-line file on a 100-px bar would be 0.1 px
        // — must be inflated to MinHitHeight (4 px) on both sides.
        var hunk = Hunk(newStart: 500, newCount: 1, oldStart: 500, oldCount: 1,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 1000, rightTotalLines: 1000, barHeight: 100);

        layouts[0].LeftRect!.Value.Height.Should().Be(HunkOverviewBarGeometry.MinHitHeight);
        layouts[0].RightRect!.Value.Height.Should().Be(HunkOverviewBarGeometry.MinHitHeight);
    }

    [Fact]
    public void ComputeLayouts_ClampsInflatedRectInsideBar_AtTopEdge()
    {
        var hunk = Hunk(newStart: 1, newCount: 1, oldStart: 1, oldCount: 1,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 1000, rightTotalLines: 1000, barHeight: 100);

        layouts[0].LeftRect!.Value.Top.Should().Be(0);
        layouts[0].RightRect!.Value.Top.Should().Be(0);
    }

    [Fact]
    public void ComputeLayouts_ClampsInflatedRectInsideBar_AtBottomEdge()
    {
        var hunk = Hunk(newStart: 1000, newCount: 1, oldStart: 1000, oldCount: 1,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = Layouts(new[] { hunk }, leftTotalLines: 1000, rightTotalLines: 1000, barHeight: 100);

        (layouts[0].LeftRect!.Value.Bottom).Should().BeLessThanOrEqualTo(100);
        (layouts[0].RightRect!.Value.Bottom).Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void ComputeLayouts_ReturnsEmpty_WhenBarHasNoSize()
    {
        var hunk = Hunk(newStart: 5, newCount: 1, oldStart: 5, oldCount: 1,
            kinds: new[] { DiffLineKind.Modified });

        Layouts(new[] { hunk }, leftTotalLines: 100, rightTotalLines: 100, barHeight: 0)
            .Should().BeEmpty();
        HunkOverviewBarGeometry.ComputeLayouts(new[] { hunk }, 100, 100, 0, 200, ColumnWidth)
            .Should().BeEmpty();
    }

    [Fact]
    public void ComputeLayouts_ClampsColumnWidth_WhenItExceedsHalfTheBar()
    {
        // 32-pixel bar should never let either column be wider than the
        // bar can fit two columns + ≥1 px ribbon gutter.
        var hunk = Hunk(newStart: 5, newCount: 5, oldStart: 5, oldCount: 5,
            kinds: new[] { DiffLineKind.Modified });

        var layouts = HunkOverviewBarGeometry.ComputeLayouts(
            new[] { hunk }, 100, 100, barWidth: 8, barHeight: 100, columnWidth: 10);

        // Bar is only 8 px wide; column should clamp to (8-1)/2 = 3.5.
        layouts[0].LeftRect!.Value.Width.Should().BeLessThanOrEqualTo(3.5 + 0.01);
        layouts[0].RightRect!.Value.Width.Should().BeLessThanOrEqualTo(3.5 + 0.01);
    }

    // ---------- HitTest ----------

    [Fact]
    public void HitTest_ReturnsIndexWhenClickInsideLeftRect()
    {
        var hunks = new[]
        {
            Hunk(newStart: 50, newCount: 10, oldStart: 50, oldCount: 10,
                kinds: new[] { DiffLineKind.Modified }),
        };
        var layouts = Layouts(hunks, 100, 100, barHeight: 200);

        // Click in the left column at y=100 (rect spans 98..118).
        HunkOverviewBarGeometry.HitTest(layouts, new Point(2, 100)).Should().Be(0);
    }

    [Fact]
    public void HitTest_ReturnsIndexWhenClickInsideRightRect()
    {
        var hunks = new[]
        {
            Hunk(newStart: 50, newCount: 10, oldStart: 50, oldCount: 10,
                kinds: new[] { DiffLineKind.Modified }),
        };
        var layouts = Layouts(hunks, 100, 100, barHeight: 200);

        // Right column starts at barWidth-columnWidth = 22.
        HunkOverviewBarGeometry.HitTest(layouts, new Point(25, 100)).Should().Be(0);
    }

    [Fact]
    public void HitTest_ReturnsIndexWhenClickInsideTrapezoidRibbon()
    {
        // Modified hunk with left rect at lines 50..59 and right rect also
        // at 50..59 (same Y-range on both columns) — ribbon is a parallel
        // band, so the midpoint Y of the ribbon is inside both rects' Y
        // range. The click is in the middle of the bar (in the ribbon zone).
        var hunks = new[]
        {
            Hunk(newStart: 50, newCount: 10, oldStart: 50, oldCount: 10,
                kinds: new[] { DiffLineKind.Modified }),
        };
        var layouts = Layouts(hunks, 100, 100, barHeight: 200);

        // Ribbon spans x in [10, 22]; Y range matches the rects' (98..118).
        HunkOverviewBarGeometry.HitTest(layouts, new Point(16, 108)).Should().Be(0);
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_WhenClickIsAboveAllMarkers()
    {
        var hunks = new[]
        {
            Hunk(newStart: 50, newCount: 5, oldStart: 50, oldCount: 5,
                kinds: new[] { DiffLineKind.Modified }),
            Hunk(newStart: 80, newCount: 5, oldStart: 80, oldCount: 5,
                kinds: new[] { DiffLineKind.Modified }),
        };
        var layouts = Layouts(hunks, 100, 100, barHeight: 200);

        // y=10 is well above hunks 50..59 and 80..84 in a 200 px bar.
        HunkOverviewBarGeometry.HitTest(layouts, new Point(2, 10)).Should().Be(-1);
    }

    [Fact]
    public void HitTest_PicksClosestMarker_WhenInflatedRectsOverlap()
    {
        // Two adjacent 1-line hunks in a tall file: both inflate to 4 px
        // and overlap. The click closer to the first marker's midpoint
        // wins.
        var hunks = new[]
        {
            Hunk(newStart: 500, newCount: 1, oldStart: 500, oldCount: 1,
                kinds: new[] { DiffLineKind.Modified }),
            Hunk(newStart: 510, newCount: 1, oldStart: 510, oldCount: 1,
                kinds: new[] { DiffLineKind.Modified }),
        };
        var layouts = Layouts(hunks, 1000, 1000, barHeight: 100);

        // First marker's midpoint ~49.95, second ~50.95. Click at 49.5
        // should pick hunk 0.
        HunkOverviewBarGeometry.HitTest(layouts, new Point(2, 49.5)).Should().Be(0);
    }

    [Fact]
    public void HitTest_RibbonNotHit_ForPureAddOrDelete()
    {
        // Pure-add: only the right rect exists; clicks in the middle of
        // the bar (where the ribbon would be) must miss.
        var hunks = new[]
        {
            Hunk(newStart: 50, newCount: 10, oldStart: 0, oldCount: 0,
                kinds: new[] { DiffLineKind.Inserted }),
        };
        var layouts = Layouts(hunks, 100, 100, barHeight: 200);

        // x=16 is between the columns; for a pure-add there's no ribbon
        // there, so the hit-test must miss.
        HunkOverviewBarGeometry.HitTest(layouts, new Point(16, 108)).Should().Be(-1);
    }

    [Fact]
    public void HitTest_PureDeleteHunkWithContext_RightColumnClickMisses()
    {
        // Regression: with context lines around a pure-delete the right
        // rect used to be emitted (NewLineCount > 0), making clicks in
        // the right column register a false hit. After gating by Shape
        // the right rect is null and the click must miss.
        var hunks = new[]
        {
            Hunk(newStart: 50, newCount: 6, oldStart: 50, oldCount: 7,
                kinds: new[]
                {
                    DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
                    DiffLineKind.Deleted,
                    DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
                }),
        };
        var layouts = Layouts(hunks, 100, 100, barHeight: 200);

        // Right column spans x=22..32; click at x=25 in the middle of the
        // hunk's vertical range.
        HunkOverviewBarGeometry.HitTest(layouts, new Point(25, 105)).Should().Be(-1);
        // Left column click (x=2) still hits the deletion marker.
        HunkOverviewBarGeometry.HitTest(layouts, new Point(2, 105)).Should().Be(0);
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_OnEmptyHunkList()
    {
        var layouts = Layouts(Array.Empty<DiffHunk>(), 100, 100, barHeight: 100);

        HunkOverviewBarGeometry.HitTest(layouts, new Point(2, 50)).Should().Be(-1);
    }

    // ---------- ClassifyHunk (unchanged) ----------

    [Fact]
    public void ClassifyHunk_PureInsert()
    {
        var hunk = Hunk(newStart: 5, newCount: 2, kinds: new[]
        {
            DiffLineKind.Inserted, DiffLineKind.Inserted,
        });

        HunkOverviewBarGeometry.ClassifyHunk(hunk).Should().Be(HunkChangeShape.PureInsert);
    }

    [Fact]
    public void ClassifyHunk_PureDelete()
    {
        var hunk = Hunk(newStart: 5, newCount: 0, oldStart: 5, oldCount: 2, kinds: new[]
        {
            DiffLineKind.Deleted, DiffLineKind.Deleted,
        });

        HunkOverviewBarGeometry.ClassifyHunk(hunk).Should().Be(HunkChangeShape.PureDelete);
    }

    [Fact]
    public void ClassifyHunk_Mixed_WhenBothInsertsAndDeletesPresent()
    {
        var hunk = Hunk(newStart: 5, newCount: 1, oldStart: 5, oldCount: 1, kinds: new[]
        {
            DiffLineKind.Inserted, DiffLineKind.Deleted,
        });

        HunkOverviewBarGeometry.ClassifyHunk(hunk).Should().Be(HunkChangeShape.Mixed);
    }

    [Fact]
    public void ClassifyHunk_Mixed_WhenOnlyContextOrModifiedLines()
    {
        var hunk = Hunk(newStart: 5, newCount: 1, oldStart: 5, oldCount: 1, kinds: new[]
        {
            DiffLineKind.Modified,
        });

        HunkOverviewBarGeometry.ClassifyHunk(hunk).Should().Be(HunkChangeShape.Mixed);
    }

    // ---------- ComputeViewport ----------

    [Fact]
    public void ComputeViewport_NullState_ReturnsNull()
    {
        HunkOverviewBarGeometry.ComputeViewport(
            state: null,
            leftTotalLines: 100, rightTotalLines: 100,
            barWidth: BarWidth, barHeight: 200, columnWidth: ColumnWidth)
            .Should().BeNull();
    }

    [Fact]
    public void ComputeViewport_BothSidesHaveLines_ReturnsBothRects()
    {
        // Visible window: lines 10..30 on both sides of a 100-line file
        // rendered on a 200-px bar.
        var state = new DiffViewer.Models.ViewportState(
            LeftFirstLine: 10, LeftLastLine: 30,
            RightFirstLine: 10, RightLastLine: 30);

        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, leftTotalLines: 100, rightTotalLines: 100,
            barWidth: BarWidth, barHeight: 200, columnWidth: ColumnWidth);

        band.Should().NotBeNull();
        band!.LeftRect.Should().NotBeNull();
        band.RightRect.Should().NotBeNull();
        // Top at (9/100)*200 = 18; bottom at (30/100)*200 = 60.
        band.LeftRect!.Value.Top.Should().BeApproximately(18.0, 0.01);
        band.LeftRect!.Value.Bottom.Should().BeApproximately(60.0, 0.01);
        band.RightRect!.Value.Top.Should().BeApproximately(18.0, 0.01);
        band.RightRect!.Value.Bottom.Should().BeApproximately(60.0, 0.01);
    }

    [Fact]
    public void ComputeViewport_DifferentSideLineCounts_ProducesAsymmetricBands()
    {
        // 50 lines old, 100 lines new — the same visible range on each
        // side maps to different fractions of the bar, so the band
        // becomes a trapezoid (the visual point of having per-column
        // bands).
        var state = new DiffViewer.Models.ViewportState(
            LeftFirstLine: 10, LeftLastLine: 20,
            RightFirstLine: 10, RightLastLine: 20);

        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, leftTotalLines: 50, rightTotalLines: 100,
            barWidth: BarWidth, barHeight: 200, columnWidth: ColumnWidth);

        band.Should().NotBeNull();
        // Left: (9/50)*200 = 36 .. (20/50)*200 = 80 → height 44.
        // Right: (9/100)*200 = 18 .. (20/100)*200 = 40 → height 22.
        band!.LeftRect!.Value.Height.Should().BeApproximately(44.0, 0.01);
        band.RightRect!.Value.Height.Should().BeApproximately(22.0, 0.01);
    }

    [Fact]
    public void ComputeViewport_ViewportLargerThanFile_ClampsToBarHeight()
    {
        // Editor scrolled past EOF (or short file in tall viewport): the
        // last-line value can exceed totalLines. Band must clamp to the
        // bar's full height instead of overflowing.
        var state = new DiffViewer.Models.ViewportState(
            LeftFirstLine: 1, LeftLastLine: 999,
            RightFirstLine: 1, RightLastLine: 999);

        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, leftTotalLines: 100, rightTotalLines: 100,
            barWidth: BarWidth, barHeight: 200, columnWidth: ColumnWidth);

        band.Should().NotBeNull();
        band!.LeftRect!.Value.Top.Should().Be(0);
        band.LeftRect!.Value.Bottom.Should().BeApproximately(200, 0.01);
        band.RightRect!.Value.Top.Should().Be(0);
        band.RightRect!.Value.Bottom.Should().BeApproximately(200, 0.01);
    }

    [Fact]
    public void ComputeViewport_ZeroSentinel_OmitsThatSidesRect()
    {
        // Inline mode: the visible window contains only inserted lines,
        // so the old side has no representation. UpdateViewport encodes
        // that as 0 in the LeftFirst/LeftLast pair; the geometry layer
        // turns it into a null rect, leaving a single-column band.
        var state = new DiffViewer.Models.ViewportState(
            LeftFirstLine: 0, LeftLastLine: 0,
            RightFirstLine: 10, RightLastLine: 20);

        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, leftTotalLines: 100, rightTotalLines: 100,
            barWidth: BarWidth, barHeight: 200, columnWidth: ColumnWidth);

        band.Should().NotBeNull();
        band!.LeftRect.Should().BeNull();
        band.RightRect.Should().NotBeNull();
    }

    [Fact]
    public void ComputeViewport_LeftTotalLinesZero_ReturnsNullLeftRect()
    {
        // Defensive: pure-new-file viewing (left side empty).
        var state = new DiffViewer.Models.ViewportState(
            LeftFirstLine: 1, LeftLastLine: 1,
            RightFirstLine: 10, RightLastLine: 20);

        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, leftTotalLines: 0, rightTotalLines: 100,
            barWidth: BarWidth, barHeight: 200, columnWidth: ColumnWidth);

        band.Should().NotBeNull();
        band!.LeftRect.Should().BeNull();
        band.RightRect.Should().NotBeNull();
    }

    // ---------- IsInsideBand ----------

    [Fact]
    public void IsInsideBand_PointInLeftRect_Hits()
    {
        var state = new DiffViewer.Models.ViewportState(10, 30, 10, 30);
        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, 100, 100, BarWidth, 200, ColumnWidth)!;

        // Left rect: x in [0,10], y in [18,60].
        HunkOverviewBarGeometry.IsInsideBand(band, new Point(2, 30)).Should().BeTrue();
    }

    [Fact]
    public void IsInsideBand_PointInRibbon_Hits()
    {
        var state = new DiffViewer.Models.ViewportState(10, 30, 10, 30);
        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, 100, 100, BarWidth, 200, ColumnWidth)!;

        // Ribbon: x in [10, 22] at the same Y-range.
        HunkOverviewBarGeometry.IsInsideBand(band, new Point(16, 30)).Should().BeTrue();
    }

    [Fact]
    public void IsInsideBand_PointOutsideBothColumnsAndRibbon_Misses()
    {
        var state = new DiffViewer.Models.ViewportState(10, 30, 10, 30);
        var band = HunkOverviewBarGeometry.ComputeViewport(
            state, 100, 100, BarWidth, 200, ColumnWidth)!;

        // Well above the band.
        HunkOverviewBarGeometry.IsInsideBand(band, new Point(2, 5)).Should().BeFalse();
    }
}
