using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public class DiffServiceTests
{
    private static readonly DiffOptions Default = new();

    [Fact]
    public void EmptyEqualsEmpty_ProducesNoHunks()
    {
        var svc = new DiffService();
        var r = svc.ComputeDiff("", "", Default);

        r.Hunks.Should().BeEmpty();
        r.FallbackReason.Should().Be(DiffFallbackReason.None);
    }

    [Fact]
    public void IdenticalContent_ProducesNoHunks()
    {
        var svc = new DiffService();
        var r = svc.ComputeDiff("a\nb\nc\n", "a\nb\nc\n", Default);

        r.Hunks.Should().BeEmpty();
    }

    [Fact]
    public void PureInsert_ProducesOneHunk()
    {
        var svc = new DiffService();
        var r = svc.ComputeDiff("a\nb\nc\n", "a\nb\nc\nd\n", Default);

        r.Hunks.Should().HaveCount(1);
        r.Hunks[0].Lines.Last().Kind.Should().Be(DiffLineKind.Inserted);
        r.Hunks[0].Lines.Last().Text.Should().Be("d");
    }

    [Fact]
    public void PureDelete_ProducesOneHunk()
    {
        var svc = new DiffService();
        var r = svc.ComputeDiff("a\nb\nc\nd\n", "a\nb\nc\n", Default);

        r.Hunks.Should().HaveCount(1);
        var deleted = r.Hunks[0].Lines.Where(l => l.Kind == DiffLineKind.Deleted).ToList();
        deleted.Should().ContainSingle(l => l.Text == "d");
    }

    [Fact]
    public void Modification_EmitsDeleteThenInsertInOneHunk()
    {
        var svc = new DiffService();
        var r = svc.ComputeDiff("a\nfoo\nc\n", "a\nbar\nc\n", Default);

        r.Hunks.Should().HaveCount(1);
        var hunk = r.Hunks[0];
        hunk.Lines.Where(l => l.Kind == DiffLineKind.Deleted).Should().ContainSingle(l => l.Text == "foo");
        hunk.Lines.Where(l => l.Kind == DiffLineKind.Inserted).Should().ContainSingle(l => l.Text == "bar");
    }

    [Fact]
    public void IgnoreWhitespace_TreatsLineWithDifferentSpacingAsEqual()
    {
        var svc = new DiffService();
        var r1 = svc.ComputeDiff("a b c\n", "a   b\tc\n", Default);
        var r2 = svc.ComputeDiff("a b c\n", "a   b\tc\n", new DiffOptions(IgnoreWhitespace: true));

        r1.Hunks.Should().NotBeEmpty();
        r2.Hunks.Should().BeEmpty();
    }

    [Fact]
    public void HasVisibleDifferences_ShortCircuitsOnEqual()
    {
        var svc = new DiffService();
        svc.HasVisibleDifferences("hello", "hello", Default).Should().BeFalse();
    }

    [Fact]
    public void HasVisibleDifferences_ReturnsTrueWhenLinesDiffer()
    {
        var svc = new DiffService();
        svc.HasVisibleDifferences("a\n", "b\n", Default).Should().BeTrue();
    }

    [Fact]
    public void HasVisibleDifferences_RespectsIgnoreWhitespace()
    {
        var svc = new DiffService();
        svc.HasVisibleDifferences("a b\n", "a  b\n", new DiffOptions(IgnoreWhitespace: false)).Should().BeTrue();
        svc.HasVisibleDifferences("a b\n", "a  b\n", new DiffOptions(IgnoreWhitespace: true)).Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_AboveLineCap_ReportsInputTooLargeFallback()
    {
        var svc = new DiffService();
        // 100 lines vs cap 10 → triggers cap.
        var left = string.Concat(Enumerable.Range(0, 100).Select(i => $"line{i}\n"));
        var right = string.Concat(Enumerable.Range(0, 100).Select(i => $"line{i}\n"));

        var r = svc.ComputeDiff(left, right, new DiffOptions(MaxLines: 10));

        r.FallbackReason.Should().Be(DiffFallbackReason.InputTooLarge);
        // Equal content → still no hunks even on the fallback path.
        r.Hunks.Should().BeEmpty();
    }

    [Fact]
    public void ComputeIntraLineDiff_HighlightsChangedWord()
    {
        var svc = new DiffService();
        var pieces = svc.ComputeIntraLineDiff("the quick brown fox", "the slow brown fox", ignoreWhitespace: false);

        pieces.Where(p => p.Kind == IntraLinePieceKind.Deleted).Select(p => p.Text)
            .Should().Contain("quick");
        pieces.Where(p => p.Kind == IntraLinePieceKind.Inserted).Select(p => p.Text)
            .Should().Contain("slow");
    }

    [Fact]
    public void ContextLinesAreIncludedAroundChange()
    {
        var svc = new DiffService();
        var left = "a\nb\nc\nd\ne\nf\n";
        var right = "a\nb\nc\nX\ne\nf\n";
        var r = svc.ComputeDiff(left, right, Default);

        r.Hunks.Should().HaveCount(1);
        var hunk = r.Hunks[0];
        // Change is on line 4 (1-based). With 3 context lines, hunk should span from line 1-6.
        hunk.OldStartLine.Should().Be(1);
        hunk.OldLineCount.Should().Be(6);
        hunk.NewStartLine.Should().Be(1);
        hunk.NewLineCount.Should().Be(6);
    }
}
