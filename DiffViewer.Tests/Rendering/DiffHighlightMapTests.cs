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

    [Fact]
    public void FromHunks_IntraLineEnabled_LowSimilarityPair_SuppressesSpans()
    {
        // Two paired lines that share only whitespace and a handful of
        // delimiters ("//", parens, "the"/"so") should NOT get noisy
        // intra-line spans — the line-level red/yellow background is the
        // honest signal for "these lines are unrelated".
        const string oldLine = "// rect(s) are present so the user can find the marker the";
        const string newLine = "// (left rect + ribbon + right rect) as a single polygon so it";
        var hunks = _diff.ComputeDiff(oldLine, newLine, new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        // Lines are still marked Modified at the line level…
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Modified);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Modified);
        // …but the intra-line spans are suppressed.
        map.LeftLines[1].IntraLineSpans.Should().NotBeNull().And.BeEmpty();
        map.RightLines[1].IntraLineSpans.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_HighSimilarityPair_KeepsSpans()
    {
        // Two paired lines that share most of their content (identical
        // indentation, identical return keyword, only the value differs)
        // should keep intra-line spans so the user sees the change at a
        // glance.
        var hunks = _diff.ComputeDiff(
            "        return null;",
            "        return Empty;",
            new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        map.LeftLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
        map.RightLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_OldFullyContainedInNew_KeepsSpans()
    {
        // The old line's content is fully preserved in the new line — only
        // a trailing comment was appended. Intra-line should fire AND only
        // the truly appended content (after the original line ends) should
        // be highlighted on the new side. The chunker merges `";` (old)
        // and `";  ` (new) into different delimiter chunks; without
        // post-processing the boundary leaks into the highlight.
        const string oldLine = "                toVersion = \"v9\";";
        const string newLine = "                toVersion = \"v9\";  // a long appended comment that is much longer than the original line";
        var hunks = _diff.ComputeDiff(oldLine, newLine, new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        var rightSpans = map.RightLines[1].IntraLineSpans;
        rightSpans.Should().NotBeNull().And.NotBeEmpty();
        // The first inserted span must start at or after the end of the
        // shared old-line content — anything earlier means the chunker
        // boundary leaked into the highlight.
        rightSpans!.Min(s => s.StartColumn).Should().BeGreaterThanOrEqualTo(oldLine.Length);
        // And the highlight must extend to the very end of the new line.
        rightSpans.Max(s => s.EndColumn).Should().Be(newLine.Length);

        // Old side has nothing actually deleted.
        map.LeftLines[1].IntraLineSpans.Should().NotBeNull();
        map.LeftLines[1].IntraLineSpans!.Should().BeEmpty();
    }
}
