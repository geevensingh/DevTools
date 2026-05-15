using System;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class RelativeTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact] public void LessThan60Seconds_JustNow()
        => RelativeTimeFormatter.Format(Now.AddSeconds(-30), Now).Should().Be("just now");

    [Fact] public void LessThan60Minutes_MinutesAgo()
        => RelativeTimeFormatter.Format(Now.AddMinutes(-5), Now).Should().Be("5m ago");

    [Fact] public void LessThan24Hours_HoursAgo()
        => RelativeTimeFormatter.Format(Now.AddHours(-3), Now).Should().Be("3h ago");

    [Fact] public void LessThan7Days_DayOfWeek()
    {
        var label = RelativeTimeFormatter.Format(Now.AddDays(-2), Now);
        // Day-of-week is locale-sensitive; just assert it's a short token, not the date.
        label.Length.Should().BeInRange(2, 4);
        label.Should().NotContain("ago");
    }

    [Fact] public void SameYearOlder_MonthDay()
    {
        // 30 days back, same year — should be "MMM d" formatted.
        var label = RelativeTimeFormatter.Format(Now.AddDays(-30), Now);
        label.Should().NotContain("ago");
        label.Should().NotContain("-");
    }

    [Fact] public void DifferentYear_IsoDate()
    {
        var label = RelativeTimeFormatter.Format(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), Now);
        label.Should().StartWith("2024-");
    }

    [Fact] public void FutureValue_TreatedAsJustNow()
        => RelativeTimeFormatter.Format(Now.AddSeconds(30), Now).Should().Be("just now");
}
