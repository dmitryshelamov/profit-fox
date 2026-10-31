using ProfitFox.Domain.Entities.MarketData.Enums;
using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.MarketData.Enums;

public class IntervalCandleEndTests
{
    [TestCase(Interval.OneMinute, "2024-01-01T00:00:00Z", "2024-01-01T00:01:00Z")]
    [TestCase(Interval.ThreeMinutes, "2024-01-01T00:03:00Z", "2024-01-01T00:06:00Z")]
    [TestCase(Interval.FiveMinutes, "2024-01-01T00:05:00Z", "2024-01-01T00:10:00Z")]
    [TestCase(Interval.FifteenMinutes, "2024-01-01T00:45:00Z", "2024-01-01T01:00:00Z")]
    [TestCase(Interval.ThirtyMinutes, "2024-01-01T23:30:00Z", "2024-01-02T00:00:00Z")]
    [TestCase(Interval.OneHour, "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z")]
    [TestCase(Interval.TwoHours, "2024-01-01T10:00:00Z", "2024-01-01T12:00:00Z")]
    [TestCase(Interval.FourHours, "2024-01-01T20:00:00Z", "2024-01-02T00:00:00Z")]
    [TestCase(Interval.SixHours, "2024-01-01T18:00:00Z", "2024-01-02T00:00:00Z")]
    [TestCase(Interval.TwelveHours, "2024-01-01T12:00:00Z", "2024-01-02T00:00:00Z")]
    [TestCase(Interval.OneDay, "2024-12-31T00:00:00Z", "2025-01-01T00:00:00Z")]
    [TestCase(Interval.OneWeek, "2024-01-01T00:00:00Z", "2024-01-08T00:00:00Z")]
    [TestCase(Interval.OneMonth, "2024-01-01T00:00:00Z", "2024-02-01T00:00:00Z")]
    [TestCase(Interval.OneMonth, "2024-02-01T00:00:00Z", "2024-03-01T00:00:00Z")]
    [TestCase(Interval.OneMonth, "2023-02-01T00:00:00Z", "2023-03-01T00:00:00Z")]
    [TestCase(Interval.OneMonth, "2023-12-01T00:00:00Z", "2024-01-01T00:00:00Z")]
    public void Candle_end_follows_the_interval(Interval interval, string openTime, string expectedEnd)
    {
        interval.CandleEnd(DateTimeOffset.Parse(openTime)).ShouldBe(DateTimeOffset.Parse(expectedEnd));
    }
}
