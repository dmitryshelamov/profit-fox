using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>Decision 2: a series of an interval warms up from stored candles and grows by the minutes of the stream.</summary>
public class IdeaSeriesTests
{
    private static readonly DateTimeOffset Ten = DateTimeOffset.Parse("2024-03-01T10:00:00Z");

    // Task 2.3: the warm-up up to 10:37 and three minutes of the stream close the candle of 10:35 as the join of the whole period does.
    [Test]
    public void Warm_up_and_minutes_of_the_stream_give_the_candles_of_the_whole_period()
    {
        IReadOnlyList<IdeaCandle> minutes = IdeaFixtures.Minutes();
        var series = new IdeaSeries(Interval.FiveMinutes);

        series.Warm(
            IdeaCandles.FromMinutes(Interval.FiveMinutes, minutes, Ten, Ten.AddMinutes(35)),
            [.. minutes.Where(minute => minute.OpenTime >= Ten.AddMinutes(35) && minute.OpenTime < Ten.AddMinutes(37))]);

        foreach (IdeaCandle minute in minutes.Where(minute => minute.OpenTime >= Ten.AddMinutes(37) && minute.OpenTime < Ten.AddMinutes(40)))
        {
            series.Add(minute);
        }

        series.Closed.ShouldBe(IdeaCandles.FromMinutes(Interval.FiveMinutes, minutes, Ten, Ten.AddMinutes(40)));
    }

    [Test]
    public void Series_keeps_the_last_220_candles()
    {
        IdeaCandle[] candles = [.. Enumerable.Range(0, 230).Select(index => new IdeaCandle(Ten.AddMinutes(5 * index), 1m, 1m, 1m, 1m, 1m))];
        var series = new IdeaSeries(Interval.FiveMinutes);

        series.Warm(candles, []);

        series.Closed.ShouldBe(candles[10..]);
    }

    [Test]
    public void A_candle_that_closes_on_a_full_series_pushes_out_the_oldest()
    {
        IdeaCandle[] candles = [.. Enumerable.Range(0, 220).Select(index => new IdeaCandle(Ten.AddMinutes(5 * index), 1m, 1m, 1m, 1m, 1m))];
        var series = new IdeaSeries(Interval.FiveMinutes);
        series.Warm(candles, []);

        foreach (int minute in Enumerable.Range(0, 5))
        {
            series.Add(new IdeaCandle(Ten.AddMinutes(1100 + minute), 2m, 2m, 2m, 2m, 1m));
        }

        series.Closed.Count.ShouldBe(220);
        (series.Closed[0], series.Closed[^1]).ShouldBe((candles[1], new IdeaCandle(Ten.AddMinutes(1100), 2m, 2m, 2m, 2m, 5m)));
    }

    // The last candle of a setup is the window of the last 5 minutes, so the candles it is measured against end before it.
    [TestCase(40, 35)]
    [TestCase(42, 35)]
    [TestCase(45, 40)]
    public void Base_candles_end_before_the_window_of_the_last_candle(int endMinute, int lastBaseEndMinute)
    {
        var series = new IdeaSeries(Interval.FiveMinutes);
        series.Warm(IdeaCandles.FromMinutes(Interval.FiveMinutes, IdeaFixtures.Minutes(), Ten, Ten.AddMinutes(endMinute - (endMinute % 5))), []);

        series.Base(Ten.AddMinutes(endMinute))[^1].OpenTime.ShouldBe(Ten.AddMinutes(lastBaseEndMinute - 5));
    }
}