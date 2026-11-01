using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.MarketData.Enums;

public class IntervalCandleCountTests
{
    [TestCase(Interval.OneHour, "2024-01-01T00:00:00Z", "2024-01-02T00:00:00Z", 24)]
    [TestCase(Interval.OneHour, "2024-01-01T00:00:00Z", "2024-01-01T00:30:00Z", 1)]
    [TestCase(Interval.OneMonth, "2024-01-01T00:00:00Z", "2024-04-01T00:00:00Z", 3)]
    [TestCase(Interval.OneDay, "2024-01-01T00:00:00Z", "2024-01-01T00:00:00Z", 0)]
    public void Period_holds_a_candle_for_every_open_time_in_it(Interval interval, string from, string to, long count)
    {
        interval.CandleCount(DateTimeOffset.Parse(from), DateTimeOffset.Parse(to)).ShouldBe(count);
    }
}