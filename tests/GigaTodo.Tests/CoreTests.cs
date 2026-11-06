using GigaTodo;
using Xunit;

namespace GigaTodo.Tests;

public class CoreTests
{
    [Fact]
    public void DedupeIsExactApartFromOuterWhitespaceAndLineEndings()
    {
        Assert.Equal(Data.Key("a", " x\r\ny ", null), Data.Key("a", "x\ny", ""));
        Assert.NotEqual(Data.Key("a", "x", "T-1"), Data.Key("a", "X", "T-1"));
        Assert.NotEqual(Data.Key("a", "x", "T-1"), Data.Key("b", "x", "T-1"));
        Assert.NotEqual(Data.Key("a", "x", "T-1"), Data.Key("a", "y", "T-1"));
    }

    [Fact]
    public void WeekdayFilterDoesNotShiftAnchor()
    {
        var s = new Recurrence { StartAt = DateTimeOffset.Parse("2026-11-06T07:00:00Z"), IntervalHours = 24, Days = [1,2,3,4,5] };
        Assert.Equal(DateTimeOffset.Parse("2026-11-09T07:00:00Z"), ScheduleMath.Next(s, s.StartAt));
        Assert.Equal(s.StartAt, ScheduleMath.Next(s, s.StartAt, true));
    }

    [Fact]
    public void FilterUsesScheduleZoneNotUtcWeekday()
    {
        var s = new Recurrence { StartAt = DateTimeOffset.Parse("2026-11-08T22:00:00Z"), IntervalHours = 24, Days = [1] };
        Assert.Equal(s.StartAt, ScheduleMath.Next(s, s.StartAt, true));
    }

    [Fact]
    public void ImpossibleWeekdayAndIntervalAreRejected()
    {
        var s = new Recurrence { StartAt = DateTimeOffset.Parse("2026-11-09T07:00:00Z"), IntervalHours = 168, Days = [2] };
        Assert.Throws<RuleException>(() => ScheduleMath.Next(s, s.StartAt, true));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public void UnsafeResultLinksAreRejected(string url) => Assert.Throws<RuleException>(() => TaskService.ValidateLinks([new("x", url)]));

    [Fact]
    public void EmptyPromptIsRejected() => Assert.Throws<RuleException>(() => TaskService.Validate(new("  ")));
}
