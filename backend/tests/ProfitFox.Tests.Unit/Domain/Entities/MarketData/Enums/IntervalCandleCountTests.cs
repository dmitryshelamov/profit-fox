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

    [Test]
    [CancelAfter(2000)]
    public void Counting_stops_one_past_the_limit_so_a_huge_period_costs_no_more_than_the_limit()
    {
        Interval.OneMinute.CandleCount(DateTimeOffset.Parse("0001-01-01T00:00:00Z"), DateTimeOffset.Parse("2100-01-01T00:00:00Z"), limit: 1000).ShouldBe(1001);
    }

    [Test]
    public void A_period_within_the_limit_is_counted_in_full()
    {
        Interval.OneHour.CandleCount(DateTimeOffset.Parse("2024-01-01T00:00:00Z"), DateTimeOffset.Parse("2024-01-02T00:00:00Z"), limit: 24).ShouldBe(24);
    }
}