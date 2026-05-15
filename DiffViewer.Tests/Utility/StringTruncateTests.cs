using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class StringTruncateTests
{
    [Fact] public void NullInput_ReturnsEmpty()
        => StringTruncate.MidTruncate(null, 10).Should().Be(string.Empty);

    [Fact] public void EmptyInput_ReturnsEmpty()
        => StringTruncate.MidTruncate("", 10).Should().Be("");

    [Fact] public void ShorterThanMax_ReturnsUnchanged()
        => StringTruncate.MidTruncate("short", 10).Should().Be("short");

    [Fact] public void EqualToMax_ReturnsUnchanged()
        => StringTruncate.MidTruncate("0123456789", 10).Should().Be("0123456789");

    [Fact] public void LongerThanMax_TruncatesMiddleWithEllipsis()
    {
        var result = StringTruncate.MidTruncate("feature/my-very-long-branch-name", 20);
        result.Should().HaveLength(20);
        result.Should().Contain("…");
        result.Should().StartWith("feature/");
        result.Should().EndWith("name");
    }

    [Fact] public void OddBudget_BiasesHead()
    {
        // max = 10 → budget = 9 → head = 5, tail = 4 → "ABCDE…MNOP"
        var truncated = StringTruncate.MidTruncate("ABCDEFGHIJKLMNOP", 10);
        truncated.Should().HaveLength(10);
        truncated[5].Should().Be('…');
        truncated.Should().StartWith("ABCDE");
        truncated.Should().EndWith("MNOP");
    }

    [Fact] public void TinyMax_FallsBackToHardCut()
    {
        StringTruncate.MidTruncate("abcdef", 2).Should().Be("ab");
    }
}
