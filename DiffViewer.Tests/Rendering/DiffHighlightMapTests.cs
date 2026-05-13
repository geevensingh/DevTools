using DiffViewer.Models;
using DiffViewer.Rendering;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class DiffHighlightMapTests
{
    private readonly DiffService _diff = new();

    [Fact]
    public void FromHunks_EmptyHunkList_ReturnsEmptyMap()
    {
        var map = DiffHighlightMap.FromHunks(
            Array.Empty<DiffHunk>(),
            diffService: null,
            enableIntraLine: false,
            ignoreWhitespace: false);

        map.LeftLines.Should().BeEmpty();
        map.RightLines.Should().BeEmpty();
    }

    [Fact]
    public void FromHunks_PureInsert_OnlyMarksRightSide()
    {
        var hunks = _diff.ComputeDiff("", "alpha\nbeta", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines.Should().BeEmpty();
        map.RightLines.Should().HaveCount(2);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[2].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void FromHunks_PureDelete_OnlyMarksLeftSide()
    {
        var hunks = _diff.ComputeDiff("alpha\nbeta", "", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.RightLines.Should().BeEmpty();
        map.LeftLines.Should().HaveCount(2);
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
    }

    [Fact]
    public void FromHunks_PairedDeleteInsert_MarksBothAsModified()
    {
        var hunks = _diff.ComputeDiff("alpha\nbeta\ngamma", "alpha\nBETA\ngamma", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines.Should().ContainKey(2);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Modified);
        map.RightLines.Should().ContainKey(2);
        map.RightLines[2].Kind.Should().Be(DiffLineKind.Modified);
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_PopulatesSpansForModifiedLines()
    {
        var hunks = _diff.ComputeDiff("hello world", "hello WORLD", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        map.LeftLines.Should().ContainKey(1);
        map.RightLines.Should().ContainKey(1);
        map.LeftLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
        map.RightLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void FromHunks_IntraLineDisabled_LeavesSpansNull()
    {
        var hunks = _diff.ComputeDiff("hello world", "hello WORLD", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].IntraLineSpans.Should().BeNull();
    }

    [Fact]
    public void FromHunks_UnequalDeletesAndInserts_PairsThenSpills()
    {
        var hunks = _diff.ComputeDiff("a\nb\nc\nd", "e\nd", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Modified);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[3].Kind.Should().Be(DiffLineKind.Deleted);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Modified);
    }

    [Fact]
    public void FromHunks_IntraLineSpans_ColumnsAreLineRelative()
    {
        var hunks = _diff.ComputeDiff("alpha beta", "alpha gamma", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        var leftSpans = map.LeftLines[1].IntraLineSpans!;
        var rightSpans = map.RightLines[1].IntraLineSpans!;

        leftSpans.All(s => s.StartColumn >= 6).Should().BeTrue();
        rightSpans.All(s => s.StartColumn >= 6).Should().BeTrue();
        leftSpans.All(s => s.Kind == IntraLineSpanKind.Deleted).Should().BeTrue();
        rightSpans.All(s => s.Kind == IntraLineSpanKind.Inserted).Should().BeTrue();
    }
}
