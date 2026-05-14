using DiffViewer.Models;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class InlineDiffBuilderTests
{
    private static DiffHunk Hunk(int oldStart, int oldLen, int newStart, int newLen,
        params (DiffLineKind Kind, string Text)[] lines)
    {
        return new DiffHunk(
            OldStartLine: oldStart,
            OldLineCount: oldLen,
            NewStartLine: newStart,
            NewLineCount: newLen,
            Lines: lines.Select(l => new DiffLine(l.Kind, OldLineNumber: null, NewLineNumber: null, Text: l.Text)).ToList(),
            FunctionContext: null);
    }

    private static InlineDiffBuilder.InlineDocument BuildFullFile(
        string left, string right, params DiffHunk[] hunks)
        => InlineDiffBuilder.BuildFullFile(left, right, hunks, DiffHighlightMap.Empty);

    [Fact]
    public void BuildFullFile_NoHunks_EmitsFullRightTextVerbatim()
    {
        var doc = BuildFullFile(
            left: "line1\nline2\nline3\n",
            right: "line1\nline2\nline3\n");

        doc.Text.Should().Be("line1\nline2\nline3\n");
        doc.LineHighlights.Should().BeEmpty();
    }

    [Fact]
    public void BuildFullFile_OneHunkInMiddle_EmitsBeforeAndAfterAsContext()
    {
        var left = "a\nb\nc\nd\ne\n";
        var right = "a\nb\nX\nd\ne\n";

        var hunk = Hunk(
            oldStart: 3, oldLen: 1, newStart: 3, newLen: 1,
            (DiffLineKind.Deleted, "c"),
            (DiffLineKind.Inserted, "X"));

        var doc = BuildFullFile(left, right, hunk);

        doc.Text.Should().Be("a\nb\n-c\n+X\nd\ne\n");
        // Output line 3 = '-c', line 4 = '+X' (1-based).
        doc.LineHighlights[3].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[4].Kind.Should().Be(DiffLineKind.Inserted);
        doc.LineHighlights.Should().HaveCount(2, "context lines are not tinted");
    }

    [Fact]
    public void BuildFullFile_HunkAtStart_EmitsNoLeadingContext()
    {
        var left = "a\nb\nc\n";
        var right = "X\nb\nc\n";

        var hunk = Hunk(
            oldStart: 1, oldLen: 1, newStart: 1, newLen: 1,
            (DiffLineKind.Deleted, "a"),
            (DiffLineKind.Inserted, "X"));

        var doc = BuildFullFile(left, right, hunk);

        doc.Text.Should().Be("-a\n+X\nb\nc\n");
        doc.LineHighlights[1].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[2].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void BuildFullFile_HunkAtEnd_EmitsNoTrailingContext()
    {
        var left = "a\nb\nc\n";
        var right = "a\nb\nX\n";

        var hunk = Hunk(
            oldStart: 3, oldLen: 1, newStart: 3, newLen: 1,
            (DiffLineKind.Deleted, "c"),
            (DiffLineKind.Inserted, "X"));

        var doc = BuildFullFile(left, right, hunk);

        doc.Text.Should().Be("a\nb\n-c\n+X\n");
    }

    [Fact]
    public void BuildFullFile_MultipleHunks_InterleavesContextCorrectly()
    {
        // Two changes far enough apart to be separate hunks.
        var left = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj\n";
        var right = "a\nB\nc\nd\ne\nf\ng\nh\nI\nj\n";

        var h1 = Hunk(
            oldStart: 2, oldLen: 1, newStart: 2, newLen: 1,
            (DiffLineKind.Deleted, "b"),
            (DiffLineKind.Inserted, "B"));
        var h2 = Hunk(
            oldStart: 9, oldLen: 1, newStart: 9, newLen: 1,
            (DiffLineKind.Deleted, "i"),
            (DiffLineKind.Inserted, "I"));

        var doc = BuildFullFile(left, right, h1, h2);

        // Expected: a, -b, +B, c, d, e, f, g, h, -i, +I, j
        doc.Text.Should().Be("a\n-b\n+B\nc\nd\ne\nf\ng\nh\n-i\n+I\nj\n");
        doc.LineHighlights[2].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[3].Kind.Should().Be(DiffLineKind.Inserted);
        doc.LineHighlights[10].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[11].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void BuildFullFile_PureInsertionAtStart_EmitsAddedLines()
    {
        var left = "a\nb\n";
        var right = "X\nY\na\nb\n";

        var hunk = Hunk(
            oldStart: 0, oldLen: 0, newStart: 1, newLen: 2,
            (DiffLineKind.Inserted, "X"),
            (DiffLineKind.Inserted, "Y"));

        var doc = BuildFullFile(left, right, hunk);

        doc.Text.Should().Be("+X\n+Y\na\nb\n");
        doc.LineHighlights[1].Kind.Should().Be(DiffLineKind.Inserted);
        doc.LineHighlights[2].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void BuildFullFile_PureDeletionInMiddle_EmitsRemovedLines()
    {
        var left = "a\nb\nc\nd\n";
        var right = "a\nd\n";

        var hunk = Hunk(
            oldStart: 2, oldLen: 2, newStart: 2, newLen: 0,
            (DiffLineKind.Deleted, "b"),
            (DiffLineKind.Deleted, "c"));

        var doc = BuildFullFile(left, right, hunk);

        doc.Text.Should().Be("a\n-b\n-c\nd\n");
        doc.LineHighlights[2].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[3].Kind.Should().Be(DiffLineKind.Deleted);
    }

    [Fact]
    public void BuildFullFile_HandlesCrlfRightSide()
    {
        var left = "a\r\nb\r\nc\r\n";
        var right = "a\r\nX\r\nc\r\n";

        var hunk = Hunk(
            oldStart: 2, oldLen: 1, newStart: 2, newLen: 1,
            (DiffLineKind.Deleted, "b"),
            (DiffLineKind.Inserted, "X"));

        var doc = BuildFullFile(left, right, hunk);

        // Output normalises to LF; the surrounding 'a' and 'c' are picked
        // up cleanly without trailing CR characters.
        doc.Text.Should().Be("a\n-b\n+X\nc\n");
    }

    [Fact]
    public void BuildFullFile_WithMap_PicksUpIntraLineSpansForPairedLines()
    {
        // Single-line modification (1 deleted, 1 inserted) with intra-line
        // spans pre-computed in the map. The inline doc should pull the
        // left spans onto the '-' line and the right spans onto the '+' line.
        // Verifies the intra-line colorizer will see the spans in inline mode.
        var left = "foo bar baz\n";
        var right = "foo XYZ baz\n";

        // DiffLine with explicit OldLineNumber/NewLineNumber so the map lookup
        // by line number works.
        var hunk = new DiffHunk(
            OldStartLine: 1, OldLineCount: 1,
            NewStartLine: 1, NewLineCount: 1,
            Lines: new[]
            {
                new DiffLine(DiffLineKind.Deleted, OldLineNumber: 1, NewLineNumber: null, Text: "foo bar baz"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 1, Text: "foo XYZ baz"),
            },
            FunctionContext: null);

        var diffService = new DiffViewer.Services.DiffService();
        var map = DiffHighlightMap.FromHunks(
            new[] { hunk }, diffService, enableIntraLine: true, ignoreWhitespace: false);

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk }, map);

        doc.Text.Should().Be("-foo bar baz\n+foo XYZ baz\n");

        // Line 1 = '-foo bar baz' (Deleted), should carry the LEFT-side spans
        // (intra-line Deleted spans covering 'bar').
        var deleted = doc.LineHighlights[1];
        deleted.Kind.Should().Be(DiffLineKind.Deleted);
        deleted.IntraLineSpans.Should().NotBeNull();
        deleted.IntraLineSpans!.Should().NotBeEmpty();
        deleted.IntraLineSpans.Should().OnlyContain(s => s.Kind == IntraLineSpanKind.Deleted);

        // Line 2 = '+foo XYZ baz' (Inserted), should carry the RIGHT-side spans
        // (intra-line Inserted spans covering 'XYZ').
        var inserted = doc.LineHighlights[2];
        inserted.Kind.Should().Be(DiffLineKind.Inserted);
        inserted.IntraLineSpans.Should().NotBeNull();
        inserted.IntraLineSpans!.Should().NotBeEmpty();
        inserted.IntraLineSpans.Should().OnlyContain(s => s.Kind == IntraLineSpanKind.Inserted);
    }

    [Fact]
    public void BuildFullFile_WithEmptyMap_LineHighlightsHaveNullSpans()
    {
        // Sanity: when the map has no entries (e.g. intra-line disabled),
        // each non-context line still gets a LineHighlight with the right
        // kind, just IntraLineSpans = null.
        var left = "a\nb\n";
        var right = "a\nB\n";

        var hunk = new DiffHunk(
            OldStartLine: 2, OldLineCount: 1,
            NewStartLine: 2, NewLineCount: 1,
            Lines: new[]
            {
                new DiffLine(DiffLineKind.Deleted, OldLineNumber: 2, NewLineNumber: null, Text: "b"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 2, Text: "B"),
            },
            FunctionContext: null);

        var doc = InlineDiffBuilder.BuildFullFile(
            left, right, new[] { hunk }, DiffHighlightMap.Empty);

        doc.LineHighlights[2].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[2].IntraLineSpans.Should().BeNull();
        doc.LineHighlights[3].Kind.Should().Be(DiffLineKind.Inserted);
        doc.LineHighlights[3].IntraLineSpans.Should().BeNull();
    }
}
