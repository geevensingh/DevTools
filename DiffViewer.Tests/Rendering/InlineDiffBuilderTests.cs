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

    [Fact]
    public void BuildFullFile_NoHunks_EmitsFullRightTextVerbatim()
    {
        var doc = InlineDiffBuilder.BuildFullFile(
            left: "line1\nline2\nline3\n",
            right: "line1\nline2\nline3\n",
            hunks: Array.Empty<DiffHunk>());

        doc.Text.Should().Be("line1\nline2\nline3\n");
        doc.LineKinds.Should().BeEmpty();
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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk });

        doc.Text.Should().Be("a\nb\n-c\n+X\nd\ne\n");
        // Output line 3 = '-c', line 4 = '+X' (1-based).
        doc.LineKinds[3].Should().Be(DiffLineKind.Deleted);
        doc.LineKinds[4].Should().Be(DiffLineKind.Inserted);
        doc.LineKinds.Should().HaveCount(2, "context lines are not tinted");
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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk });

        doc.Text.Should().Be("-a\n+X\nb\nc\n");
        doc.LineKinds[1].Should().Be(DiffLineKind.Deleted);
        doc.LineKinds[2].Should().Be(DiffLineKind.Inserted);
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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk });

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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { h1, h2 });

        // Expected: a, -b, +B, c, d, e, f, g, h, -i, +I, j
        doc.Text.Should().Be("a\n-b\n+B\nc\nd\ne\nf\ng\nh\n-i\n+I\nj\n");
        doc.LineKinds[2].Should().Be(DiffLineKind.Deleted);
        doc.LineKinds[3].Should().Be(DiffLineKind.Inserted);
        doc.LineKinds[10].Should().Be(DiffLineKind.Deleted);
        doc.LineKinds[11].Should().Be(DiffLineKind.Inserted);
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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk });

        doc.Text.Should().Be("+X\n+Y\na\nb\n");
        doc.LineKinds[1].Should().Be(DiffLineKind.Inserted);
        doc.LineKinds[2].Should().Be(DiffLineKind.Inserted);
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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk });

        doc.Text.Should().Be("a\n-b\n-c\nd\n");
        doc.LineKinds[2].Should().Be(DiffLineKind.Deleted);
        doc.LineKinds[3].Should().Be(DiffLineKind.Deleted);
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

        var doc = InlineDiffBuilder.BuildFullFile(left, right, new[] { hunk });

        // Output normalises to LF; the surrounding 'a' and 'c' are picked
        // up cleanly without trailing CR characters.
        doc.Text.Should().Be("a\n-b\n+X\nc\n");
    }
}
