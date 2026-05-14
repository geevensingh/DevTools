using DiffViewer.Models;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class HunkOverviewBarGeometryTests
{
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

    [Fact]
    public void ComputeMarkerRect_PositionsProportionally()
    {
        // 10-line hunk at line 50 in a 100-line file, on a 200-px bar:
        // top should be at (49/100)*200 = 98 px, height = (10/100)*200 = 20 px.
        var hunk = Hunk(newStart: 50, newCount: 10);

        var (y, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 100, barHeight: 200);

        y.Should().BeApproximately(98.0, 0.01);
        h.Should().BeApproximately(20.0, 0.01);
    }

    [Fact]
    public void ComputeMarkerRect_InflatesShortMarkersToMinimumHitHeight()
    {
        // 1-line hunk in a 1000-line file on a 100-px bar would natively be
        // 0.1 px tall — must be inflated to MinHitHeight (4 px).
        var hunk = Hunk(newStart: 500, newCount: 1);

        var (_, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 1000, barHeight: 100);

        h.Should().Be(HunkOverviewBarGeometry.MinHitHeight);
    }

    [Fact]
    public void ComputeMarkerRect_KeepsInflatedMarkerCenteredOnNaturalMidpoint()
    {
        var hunk = Hunk(newStart: 500, newCount: 1);

        var (y, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 1000, barHeight: 100);

        // Natural midpoint is at (499.5 / 1000) * 100 = 49.95 px.
        // After inflation to 4 px, top should be at 49.95 - 2 = 47.95.
        double mid = y + h / 2.0;
        mid.Should().BeApproximately(49.95, 0.01);
    }

    [Fact]
    public void ComputeMarkerRect_ClampsInflatedMarkerInsideBar_AtTopEdge()
    {
        // A 1-line hunk at line 1 has natural midpoint = 0 px; with 4 px
        // min height, the top would be -2; we must clamp to 0.
        var hunk = Hunk(newStart: 1, newCount: 1);

        var (y, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 1000, barHeight: 100);

        y.Should().Be(0);
        h.Should().Be(HunkOverviewBarGeometry.MinHitHeight);
    }

    [Fact]
    public void ComputeMarkerRect_ClampsInflatedMarkerInsideBar_AtBottomEdge()
    {
        // 1-line hunk at the last line — natural midpoint is at bar height
        // (or just past it after the integer math). Inflated marker must
        // fit entirely inside the bar.
        var hunk = Hunk(newStart: 1000, newCount: 1);

        var (y, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 1000, barHeight: 100);

        (y + h).Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void ComputeMarkerRect_FallsBackToOldStartLine_WhenNewStartLineIsZero()
    {
        // Pure-delete hunk: NewLineCount=0, NewStartLine=0 — but the marker
        // still needs to render at the deletion's location in the right
        // buffer (which is best approximated by OldStartLine).
        var hunk = Hunk(newStart: 0, newCount: 0, oldStart: 50, oldCount: 5);

        var (y, _) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 100, barHeight: 200);

        // Expected y = ((50-1)/100) * 200 = 98 (uses OldStartLine as fallback).
        y.Should().BeApproximately(98.0, 0.01);
    }

    [Fact]
    public void ComputeMarkerRect_ReturnsZero_WhenBarHasNoSize()
    {
        var hunk = Hunk(newStart: 5, newCount: 1);

        var (y, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunk, totalLines: 100, barHeight: 0);

        y.Should().Be(0);
        h.Should().Be(0);
    }

    [Fact]
    public void HitTest_ReturnsIndexOfClickedMarker()
    {
        var hunks = new[]
        {
            Hunk(newStart: 10, newCount: 5),  // y ≈ (9/100)*100 = 9, h = 5
            Hunk(newStart: 50, newCount: 10), // y ≈ 49, h = 10
            Hunk(newStart: 80, newCount: 5),  // y ≈ 79, h = 5
        };

        // Click at y=52 (inside hunk[1] which spans 49..59).
        var idx = HunkOverviewBarGeometry.HitTest(hunks, totalLines: 100, barHeight: 100, y: 52);

        idx.Should().Be(1);
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_WhenClickIsBetweenMarkers()
    {
        // Two well-separated hunks (each comfortably above MinHitHeight)
        // with a gap that's also wider than MinHitHeight, so a click in
        // the middle truly falls outside both markers.
        var hunks = new[]
        {
            Hunk(newStart: 10, newCount: 5),   // y=9..14
            Hunk(newStart: 80, newCount: 5),   // y=79..84
        };

        // Click at y=50, well between the two markers.
        var idx = HunkOverviewBarGeometry.HitTest(hunks, totalLines: 100, barHeight: 100, y: 50);

        idx.Should().Be(-1);
    }

    [Fact]
    public void HitTest_PicksClosestMarker_WhenInflatedRectsOverlap()
    {
        // Two adjacent single-line hunks in a 1000-line file on a 100 px
        // bar: each marker inflates to 4 px and the rects overlap. Click
        // closer to the first marker's midpoint should return index 0.
        var hunks = new[]
        {
            Hunk(newStart: 500, newCount: 1),  // mid ≈ 49.95
            Hunk(newStart: 510, newCount: 1),  // mid ≈ 50.95
        };

        var idx = HunkOverviewBarGeometry.HitTest(hunks, totalLines: 1000, barHeight: 100, y: 49.5);

        idx.Should().Be(0);
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_OnEmptyHunkList()
    {
        var hunks = Array.Empty<DiffHunk>();

        var idx = HunkOverviewBarGeometry.HitTest(hunks, totalLines: 100, barHeight: 100, y: 50);

        idx.Should().Be(-1);
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_WhenBarOrTotalLinesIsZero()
    {
        var hunks = new[] { Hunk(newStart: 10, newCount: 5) };

        HunkOverviewBarGeometry.HitTest(hunks, totalLines: 0, barHeight: 100, y: 50).Should().Be(-1);
        HunkOverviewBarGeometry.HitTest(hunks, totalLines: 100, barHeight: 0, y: 50).Should().Be(-1);
    }

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
        // A diff that only contains modified lines is mixed (it shouldn't
        // tint as pure-add or pure-delete).
        var hunk = Hunk(newStart: 5, newCount: 1, oldStart: 5, oldCount: 1, kinds: new[]
        {
            DiffLineKind.Modified,
        });

        HunkOverviewBarGeometry.ClassifyHunk(hunk).Should().Be(HunkChangeShape.Mixed);
    }

    [Fact]
    public void GetMarkerBrushes_PureInsert_ReturnsAddedBrushOnly()
    {
        var hunk = Hunk(newStart: 5, newCount: 1, kinds: new[] { DiffLineKind.Inserted });
        var scheme = DiffColorScheme.Classic;

        var (top, bottom) = HunkOverviewBarGeometry.GetMarkerBrushes(hunk, scheme);

        top.Should().BeSameAs(scheme.AddedIntraLineBackground);
        bottom.Should().BeNull();
    }

    [Fact]
    public void GetMarkerBrushes_PureDelete_ReturnsRemovedBrushOnly()
    {
        var hunk = Hunk(newStart: 5, newCount: 0, oldStart: 5, oldCount: 1, kinds: new[] { DiffLineKind.Deleted });
        var scheme = DiffColorScheme.Classic;

        var (top, bottom) = HunkOverviewBarGeometry.GetMarkerBrushes(hunk, scheme);

        top.Should().BeSameAs(scheme.RemovedIntraLineBackground);
        bottom.Should().BeNull();
    }

    [Fact]
    public void GetMarkerBrushes_Mixed_ReturnsRemovedOnTopAndAddedOnBottom()
    {
        var hunk = Hunk(newStart: 5, newCount: 1, oldStart: 5, oldCount: 1, kinds: new[]
        {
            DiffLineKind.Deleted, DiffLineKind.Inserted,
        });
        var scheme = DiffColorScheme.Classic;

        var (top, bottom) = HunkOverviewBarGeometry.GetMarkerBrushes(hunk, scheme);

        // Top half mirrors the unified-diff "-" lines, bottom half the "+" lines.
        top.Should().BeSameAs(scheme.RemovedIntraLineBackground);
        bottom.Should().BeSameAs(scheme.AddedIntraLineBackground);
    }
}
