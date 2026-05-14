using DiffViewer.Models;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class HunkOverviewBarTooltipTests
{
    // Helper: build a hunk where each DiffLine carries a real
    // OldLineNumber/NewLineNumber, so the tooltip's "changed range"
    // computation has something to read. Mirrors what
    // DiffService.BuildHunks produces (context lines on both sides
    // bracketing the actual change).
    private static DiffHunk PureDeleteHunkWithContext()
    {
        // Old buffer:  L5 L6 L7 [L8 deleted] L9 L10 L11
        // New buffer:  L5 L6 L7              L8 L9 L10
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Context, 5, 5, ""),
            new(DiffLineKind.Context, 6, 6, ""),
            new(DiffLineKind.Context, 7, 7, ""),
            new(DiffLineKind.Deleted, 8, null, ""),
            new(DiffLineKind.Context, 9, 8, ""),
            new(DiffLineKind.Context, 10, 9, ""),
            new(DiffLineKind.Context, 11, 10, ""),
        };
        return new DiffHunk(
            OldStartLine: 5, OldLineCount: 7,
            NewStartLine: 5, NewLineCount: 6,
            Lines: lines, FunctionContext: null);
    }

    private static DiffHunk PureInsertHunkWithContext()
    {
        // Old buffer:  L5 L6 L7              L8 L9 L10
        // New buffer:  L5 L6 L7 [L8 inserted] L9 L10 L11
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Context, 5, 5, ""),
            new(DiffLineKind.Context, 6, 6, ""),
            new(DiffLineKind.Context, 7, 7, ""),
            new(DiffLineKind.Inserted, null, 8, ""),
            new(DiffLineKind.Context, 8, 9, ""),
            new(DiffLineKind.Context, 9, 10, ""),
            new(DiffLineKind.Context, 10, 11, ""),
        };
        return new DiffHunk(
            OldStartLine: 5, OldLineCount: 6,
            NewStartLine: 5, NewLineCount: 7,
            Lines: lines, FunctionContext: null);
    }

    private static DiffHunk MixedHunkWithContext()
    {
        // 1 deletion + 2 insertions sandwiched between context.
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Context, 5, 5, ""),
            new(DiffLineKind.Context, 6, 6, ""),
            new(DiffLineKind.Deleted, 7, null, ""),
            new(DiffLineKind.Inserted, null, 7, ""),
            new(DiffLineKind.Inserted, null, 8, ""),
            new(DiffLineKind.Context, 8, 9, ""),
            new(DiffLineKind.Context, 9, 10, ""),
        };
        return new DiffHunk(
            OldStartLine: 5, OldLineCount: 5,
            NewStartLine: 5, NewLineCount: 6,
            Lines: lines, FunctionContext: null);
    }

    private static DiffHunk ModifiedOnlyHunk()
    {
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Context, 5, 5, ""),
            new(DiffLineKind.Modified, 6, 6, ""),
            new(DiffLineKind.Modified, 7, 7, ""),
            new(DiffLineKind.Context, 8, 8, ""),
        };
        return new DiffHunk(
            OldStartLine: 5, OldLineCount: 4,
            NewStartLine: 5, NewLineCount: 4,
            Lines: lines, FunctionContext: null);
    }

    [Fact]
    public void FormatTooltip_PureDelete_DoesNotMentionAddedLines()
    {
        var h = PureDeleteHunkWithContext();

        string tip = HunkOverviewBar.FormatTooltip(idx: 0, total: 1, h, HunkChangeShape.PureDelete);

        tip.Should().NotContain("added");
        tip.Should().Contain("removed");
        tip.Should().Contain("L8");
    }

    [Fact]
    public void FormatTooltip_PureInsert_DoesNotMentionRemovedLines()
    {
        var h = PureInsertHunkWithContext();

        string tip = HunkOverviewBar.FormatTooltip(idx: 0, total: 1, h, HunkChangeShape.PureInsert);

        tip.Should().NotContain("removed");
        tip.Should().Contain("added");
        tip.Should().Contain("L8");
    }

    [Fact]
    public void FormatTooltip_Mixed_UsesFilteredCountsNotContextInflatedCounts()
    {
        var h = MixedHunkWithContext();

        string tip = HunkOverviewBar.FormatTooltip(idx: 0, total: 1, h, HunkChangeShape.Mixed);

        // OldLineCount is 5 (incl. context); the actual delete is 1 line.
        // NewLineCount is 6 (incl. context); the actual inserts are 2 lines.
        tip.Should().Contain("old L7");        // single deleted line
        tip.Should().Contain("new L7-8 (2 lines)"); // two inserted lines
        tip.Should().NotContain("5 lines");    // OldLineCount sneak-in
        tip.Should().NotContain("6 lines");    // NewLineCount sneak-in
    }

    [Fact]
    public void FormatTooltip_ModifiedOnly_ShowsBothRanges()
    {
        // Modified lines count toward both sides. ClassifyHunk treats them
        // as Mixed (it only checks Inserted/Deleted), so the tooltip must
        // populate both ranges from the Modified entries.
        var h = ModifiedOnlyHunk();

        string tip = HunkOverviewBar.FormatTooltip(idx: 0, total: 1, h, HunkChangeShape.Mixed);

        tip.Should().Contain("old L6-7 (2 lines)");
        tip.Should().Contain("new L6-7 (2 lines)");
    }

    [Fact]
    public void FormatTooltip_Includes_HunkIndex_AndTotal()
    {
        var h = PureDeleteHunkWithContext();

        string tip = HunkOverviewBar.FormatTooltip(idx: 2, total: 7, h, HunkChangeShape.PureDelete);

        tip.Should().StartWith("Hunk 3 of 7:");
    }
}
