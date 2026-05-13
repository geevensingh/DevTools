using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public class UnifiedDiffFormatterTests
{
    [Fact]
    public void HeaderLines_UseSlashPaths()
    {
        var hunk = new DiffHunk(1, 1, 1, 1,
            new[] { new DiffLine(DiffLineKind.Context, 1, 1, "a") },
            null);

        var text = UnifiedDiffFormatter.Format("src/foo.cs", "src/foo.cs", new[] { hunk }, "a\n", "a\n");

        text.Should().StartWith("--- a/src/foo.cs\n+++ b/src/foo.cs\n@@");
    }

    [Fact]
    public void HunkHeader_HasOldAndNewLineCounts()
    {
        var hunk = new DiffHunk(2, 3, 5, 4,
            new[] { new DiffLine(DiffLineKind.Context, 2, 5, "x") },
            null);

        var text = UnifiedDiffFormatter.Format("a", "a", new[] { hunk }, "ignored\nx\n", "ignored\n\n\n\nx\n");

        text.Should().Contain("@@ -2,3 +5,4 @@");
    }

    [Fact]
    public void EmittedLinesPreserveOriginalCrLfTerminators()
    {
        var svc = new DiffService();
        // CRLF working tree + a one-line edit.
        var left = "alpha\r\nbeta\r\ngamma\r\n";
        var right = "alpha\r\nDELTA\r\ngamma\r\n";

        var r = svc.ComputeDiff(left, right, new DiffOptions());
        var text = svc.FormatUnified("file.txt", "file.txt", r.Hunks, left, right);

        // Both the deleted "beta" line and the inserted "DELTA" line must end with CRLF.
        text.Should().Contain("-beta\r\n");
        text.Should().Contain("+DELTA\r\n");
    }

    [Fact]
    public void NoNewlineAtEndOfFile_EmitsMarkerAfterAffectedLine()
    {
        var svc = new DiffService();
        var left = "alpha\nbeta";        // no trailing newline
        var right = "alpha\nGAMMA";      // also no trailing newline

        var r = svc.ComputeDiff(left, right, new DiffOptions());
        var text = svc.FormatUnified("f", "f", r.Hunks, left, right);

        text.Should().Contain("-beta\n\\ No newline at end of file\n");
        text.Should().Contain("+GAMMA\n\\ No newline at end of file\n");
    }

    [Fact]
    public void NoNewlineMarker_AppearsForTrailingContextLineWhenSourceLacksNewline()
    {
        var svc = new DiffService();
        // Last line of file has no terminator; an earlier line is changed.
        // Per git's unified-diff convention, the trailing context line still
        // triggers the "\ No newline at end of file" marker because the source
        // file genuinely has no newline at EOF.
        var left = "alpha\nbeta\ngamma";
        var right = "alpha\nBETA\ngamma";

        var r = svc.ComputeDiff(left, right, new DiffOptions());
        var text = svc.FormatUnified("f", "f", r.Hunks, left, right);

        text.Should().Contain("\\ No newline at end of file");
    }

    [Fact]
    public void NoNewlineMarker_AbsentWhenBothFilesEndWithNewline()
    {
        var svc = new DiffService();
        var left = "alpha\nbeta\ngamma\n";
        var right = "alpha\nBETA\ngamma\n";

        var r = svc.ComputeDiff(left, right, new DiffOptions());
        var text = svc.FormatUnified("f", "f", r.Hunks, left, right);

        text.Should().NotContain("\\ No newline at end of file");
    }

    [Fact]
    public void LineSplitter_PreservesMixedTerminators()
    {
        var lines = LineSplitter.Split("a\r\nb\nc\rd");

        lines.Should().HaveCount(4);
        lines[0].Should().Be(new LineSplitter.Line("a", "\r\n"));
        lines[1].Should().Be(new LineSplitter.Line("b", "\n"));
        lines[2].Should().Be(new LineSplitter.Line("c", "\r"));
        lines[3].Should().Be(new LineSplitter.Line("d", ""));
    }

    [Fact]
    public void LineSplitter_TrailingNewlineDoesNotProduceEmptyTrailingLine()
    {
        var lines = LineSplitter.Split("a\nb\n");
        lines.Should().HaveCount(2);
        lines[0].Should().Be(new LineSplitter.Line("a", "\n"));
        lines[1].Should().Be(new LineSplitter.Line("b", "\n"));
    }
}
