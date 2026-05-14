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

        doc.Text.Should().Be("a\nb\nc\nX\nd\ne\n");
        // Output line 3 = deleted 'c', line 4 = inserted 'X' (1-based).
        // No prefix character — kind is signalled via LineHighlights only.
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

        doc.Text.Should().Be("a\nX\nb\nc\n");
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

        doc.Text.Should().Be("a\nb\nc\nX\n");
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

        // Expected: a, b(deleted), B(inserted), c, d, e, f, g, h, i(deleted), I(inserted), j
        doc.Text.Should().Be("a\nb\nB\nc\nd\ne\nf\ng\nh\ni\nI\nj\n");
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

        doc.Text.Should().Be("X\nY\na\nb\n");
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

        doc.Text.Should().Be("a\nb\nc\nd\n");
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
        doc.Text.Should().Be("a\nb\nX\nc\n");
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

        doc.Text.Should().Be("foo bar baz\nfoo XYZ baz\n");

        // Line 1 = 'foo bar baz' (Deleted), should carry the LEFT-side spans
        // (intra-line Deleted spans covering 'bar').
        var deleted = doc.LineHighlights[1];
        deleted.Kind.Should().Be(DiffLineKind.Deleted);
        deleted.IntraLineSpans.Should().NotBeNull();
        deleted.IntraLineSpans!.Should().NotBeEmpty();
        deleted.IntraLineSpans.Should().OnlyContain(s => s.Kind == IntraLineSpanKind.Deleted);

        // Line 2 = 'foo XYZ baz' (Inserted), should carry the RIGHT-side spans
        // (intra-line Inserted spans covering 'XYZ').
        var inserted = doc.LineHighlights[2];
        inserted.Kind.Should().Be(DiffLineKind.Inserted);
        inserted.IntraLineSpans.Should().NotBeNull();
        inserted.IntraLineSpans!.Should().NotBeEmpty();
        inserted.IntraLineSpans.Should().OnlyContain(s => s.Kind == IntraLineSpanKind.Inserted);

        // Spans are NOT shifted: BuildFullFile emits lines verbatim (no
        // +/- prefix), so the colorizer's lineStart + StartColumn arithmetic
        // lands exactly on the changed characters. "bar" / "XYZ" sit at
        // columns 4..7 in the raw line text — and the displayed text IS the
        // raw line text.
        deleted.IntraLineSpans.Should().ContainSingle()
            .Which.StartColumn.Should().Be(4);
        deleted.IntraLineSpans.Single().EndColumn.Should().Be(7);
        inserted.IntraLineSpans.Should().ContainSingle()
            .Which.StartColumn.Should().Be(4);
        inserted.IntraLineSpans.Single().EndColumn.Should().Be(7);
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

    [Fact]
    public void BuildFullFile_HunkWithInternalContextLines_EmitsThemVerbatim_NoIndent()
    {
        // Regression: when a hunk's Lines list contains DiffLineKind.Context
        // entries (this happens when DiffPlex's hunk-merging combines two
        // nearby change-blocks separated by a few unchanged lines), the
        // earlier builder gave them a single-space prefix while emitting
        // outside-hunk lines verbatim. Result: indented-by-1 context lines
        // around the diff, which is exactly the bug the screenshot caught.
        //
        // The contract now: every line — context, deleted, inserted —
        // is emitted verbatim with no prefix. Original column positions
        // are preserved, so context inside the hunk lines up with context
        // outside the hunk.
        var left = "    one\n    two\n    three\n    four\n    five\n    six\n";
        var right = "    one\n    two\n    THREE\n    four\n    five\n    six\n";

        // Hunk synthesised to cover lines 2..5 with an internal Context
        // line at output position 'two'. This is the shape DiffService
        // produces when merging produces a hunk wider than the change.
        var hunk = new DiffHunk(
            OldStartLine: 2, OldLineCount: 3,
            NewStartLine: 2, NewLineCount: 3,
            Lines: new[]
            {
                new DiffLine(DiffLineKind.Context,  OldLineNumber: 2,    NewLineNumber: 2,    Text: "    two"),
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 3,    NewLineNumber: null, Text: "    three"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 3,    Text: "    THREE"),
                new DiffLine(DiffLineKind.Context,  OldLineNumber: 4,    NewLineNumber: 4,    Text: "    four"),
            },
            FunctionContext: null);

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk }, DiffHighlightMap.Empty);

        // No line has a leading prefix character. The "    two" / "    four"
        // context lines INSIDE the hunk are indented identically to "    one"
        // / "    five" / "    six" OUTSIDE the hunk — no off-by-one.
        doc.Text.Should().Be("    one\n    two\n    three\n    THREE\n    four\n    five\n    six\n");

        // Deleted/inserted lines still get LineHighlight entries (renderer
        // tints them); context lines do not.
        doc.LineHighlights.Should().HaveCount(2);
        doc.LineHighlights[3].Kind.Should().Be(DiffLineKind.Deleted);
        doc.LineHighlights[4].Kind.Should().Be(DiffLineKind.Inserted);
    }

    // ---------- LineToSourceLines mapping ----------

    /// <summary>
    /// Build a hunk with explicit per-line source line numbers — the
    /// existing <see cref="Hunk"/> helper sets them to <c>null</c>, which
    /// breaks any test that needs the inline mapping to project back
    /// onto specific source lines.
    /// </summary>
    private static DiffHunk HunkN(int oldStart, int oldLen, int newStart, int newLen,
        params (DiffLineKind Kind, int? OldLn, int? NewLn, string Text)[] lines) =>
        new DiffHunk(
            OldStartLine: oldStart,
            OldLineCount: oldLen,
            NewStartLine: newStart,
            NewLineCount: newLen,
            Lines: lines.Select(l => new DiffLine(l.Kind, l.OldLn, l.NewLn, l.Text)).ToList(),
            FunctionContext: null);

    [Fact]
    public void BuildFullFile_NoHunks_LineToSourceLines_IsIdentityMap()
    {
        var doc = BuildFullFile("a\nb\nc\n", "a\nb\nc\n");

        doc.LineToSourceLines.Should().HaveCount(3);
        doc.LineToSourceLines[0].Should().Be(((int?)1, (int?)1));
        doc.LineToSourceLines[1].Should().Be(((int?)2, (int?)2));
        doc.LineToSourceLines[2].Should().Be(((int?)3, (int?)3));
    }

    [Fact]
    public void BuildFullFile_PreHunkContext_MapsIdentityOffset()
    {
        // Hunk at line 4 of a 5-line file — output lines 1..3 are
        // pre-hunk context emitted directly from the right side. The map
        // for those lines must record (i, i) on both sides because the
        // two sides are byte-identical outside hunks.
        var left = "a\nb\nc\nd\ne\n";
        var right = "a\nb\nc\nD\ne\n";

        var hunk = HunkN(
            oldStart: 4, oldLen: 1, newStart: 4, newLen: 1,
            (DiffLineKind.Deleted, 4, null, "d"),
            (DiffLineKind.Inserted, null, 4, "D"));

        var doc = BuildFullFile(left, right, hunk);

        doc.LineToSourceLines[0].Should().Be(((int?)1, (int?)1));
        doc.LineToSourceLines[1].Should().Be(((int?)2, (int?)2));
        doc.LineToSourceLines[2].Should().Be(((int?)3, (int?)3));
    }

    [Fact]
    public void BuildFullFile_TailContext_MapsUsingCursorOffset()
    {
        // Hunk at the start — output lines 3..5 are tail context.
        // After the hunk: oldCursor=2, newCursor=2. Tail loop emits
        // right lines 2..5; each maps to (oldCursor + (i - newCursor), i).
        var left = "a\nb\nc\nd\ne\n";
        var right = "A\nb\nc\nd\ne\n";

        var hunk = HunkN(
            oldStart: 1, oldLen: 1, newStart: 1, newLen: 1,
            (DiffLineKind.Deleted, 1, null, "a"),
            (DiffLineKind.Inserted, null, 1, "A"));

        var doc = BuildFullFile(left, right, hunk);

        // Output: deleted 'a' (line 1), inserted 'A' (line 2),
        // then context b/c/d/e (lines 3..6).
        doc.LineToSourceLines[0].Should().Be(((int?)1, (int?)null));
        doc.LineToSourceLines[1].Should().Be(((int?)null, (int?)1));
        doc.LineToSourceLines[2].Should().Be(((int?)2, (int?)2));
        doc.LineToSourceLines[3].Should().Be(((int?)3, (int?)3));
        doc.LineToSourceLines[4].Should().Be(((int?)4, (int?)4));
        doc.LineToSourceLines[5].Should().Be(((int?)5, (int?)5));
    }

    [Fact]
    public void BuildFullFile_PureInsertHunk_OldLineIsNull()
    {
        // Pure-insert hunk in the middle: 2 lines added between b and c.
        var left = "a\nb\nc\n";
        var right = "a\nb\nX\nY\nc\n";

        var hunk = HunkN(
            oldStart: 3, oldLen: 0, newStart: 3, newLen: 2,
            (DiffLineKind.Inserted, null, 3, "X"),
            (DiffLineKind.Inserted, null, 4, "Y"));

        var doc = BuildFullFile(left, right, hunk);

        // Output: a, b, X, Y, c.
        doc.LineToSourceLines[0].Should().Be(((int?)1, (int?)1));     // a
        doc.LineToSourceLines[1].Should().Be(((int?)2, (int?)2));     // b
        doc.LineToSourceLines[2].Should().Be(((int?)null, (int?)3));  // X
        doc.LineToSourceLines[3].Should().Be(((int?)null, (int?)4));  // Y
        doc.LineToSourceLines[4].Should().Be(((int?)3, (int?)5));     // c
    }

    [Fact]
    public void BuildFullFile_PureDeleteHunk_NewLineIsNull()
    {
        // Pure-delete hunk in the middle: lines b and c removed.
        var left = "a\nb\nc\nd\n";
        var right = "a\nd\n";

        var hunk = HunkN(
            oldStart: 2, oldLen: 2, newStart: 2, newLen: 0,
            (DiffLineKind.Deleted, 2, null, "b"),
            (DiffLineKind.Deleted, 3, null, "c"));

        var doc = BuildFullFile(left, right, hunk);

        // Output: a, b(deleted), c(deleted), d.
        doc.LineToSourceLines[0].Should().Be(((int?)1, (int?)1));     // a
        doc.LineToSourceLines[1].Should().Be(((int?)2, (int?)null));  // b
        doc.LineToSourceLines[2].Should().Be(((int?)3, (int?)null));  // c
        doc.LineToSourceLines[3].Should().Be(((int?)4, (int?)2));     // d
    }

    [Fact]
    public void BuildFullFile_AsymmetricHunks_MapsConsistently()
    {
        // Two hunks of different shapes — verify the tail after both
        // tracks cumulative old/new offsets correctly.
        // Left:  a b c d e f g h
        // Right: a Z c d e F G h     (1 insert + 1 modify spread across two hunks)
        var left = "a\nb\nc\nd\ne\nf\ng\nh\n";
        var right = "a\nZ\nc\nd\ne\nF\nG\nh\n";

        var h1 = HunkN(
            oldStart: 2, oldLen: 1, newStart: 2, newLen: 1,
            (DiffLineKind.Deleted, 2, null, "b"),
            (DiffLineKind.Inserted, null, 2, "Z"));
        var h2 = HunkN(
            oldStart: 6, oldLen: 2, newStart: 6, newLen: 2,
            (DiffLineKind.Deleted, 6, null, "f"),
            (DiffLineKind.Deleted, 7, null, "g"),
            (DiffLineKind.Inserted, null, 6, "F"),
            (DiffLineKind.Inserted, null, 7, "G"));

        var doc = BuildFullFile(left, right, h1, h2);

        // Output (12 lines):
        //  1 a            -> (1,1)
        //  2 b deleted    -> (2, null)
        //  3 Z inserted   -> (null, 2)
        //  4 c context    -> (3, 3)
        //  5 d context    -> (4, 4)
        //  6 e context    -> (5, 5)
        //  7 f deleted    -> (6, null)
        //  8 g deleted    -> (7, null)
        //  9 F inserted   -> (null, 6)
        // 10 G inserted   -> (null, 7)
        // 11 h tail       -> (8, 8)
        doc.LineToSourceLines.Should().HaveCount(11);
        doc.LineToSourceLines[0].Should().Be(((int?)1, (int?)1));
        doc.LineToSourceLines[1].Should().Be(((int?)2, (int?)null));
        doc.LineToSourceLines[2].Should().Be(((int?)null, (int?)2));
        doc.LineToSourceLines[3].Should().Be(((int?)3, (int?)3));
        doc.LineToSourceLines[4].Should().Be(((int?)4, (int?)4));
        doc.LineToSourceLines[5].Should().Be(((int?)5, (int?)5));
        doc.LineToSourceLines[6].Should().Be(((int?)6, (int?)null));
        doc.LineToSourceLines[7].Should().Be(((int?)7, (int?)null));
        doc.LineToSourceLines[8].Should().Be(((int?)null, (int?)6));
        doc.LineToSourceLines[9].Should().Be(((int?)null, (int?)7));
        doc.LineToSourceLines[10].Should().Be(((int?)8, (int?)8));
    }
}
