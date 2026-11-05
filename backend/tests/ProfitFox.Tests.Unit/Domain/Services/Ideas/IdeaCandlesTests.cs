using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Candles of an interval from the minutes of <see cref="IdeaFixtures.Minutes" />: a minute without trades is flat at the close before it.</summary>
public class IdeaCandlesTests
{
    private static readonly DateTimeOffset Ten = DateTimeOffset.Parse("2024-03-01T10:00:00Z");

    // Reference "1 ч из 120 минут с пропусками": the second hour starts without trades, so it opens at the close of 10:59.
    [Test]
    public void Hours_join_their_minutes_and_a_gap_at_the_start_opens_at_the_close_before_it()
    {
        IdeaCandles.FromMinutes(Interval.OneHour, IdeaFixtures.Minutes(), Ten, Ten.AddHours(2)).ShouldBe(
        [
            new IdeaCandle(Ten, 50.00m, 50.82m, 47.63m, 48.27m, 20918m),
            new IdeaCandle(Ten.AddHours(1), 48.27m, 50.23m, 47.28m, 47.76m, 27696m)
        ]);
    }

    [Test]
    public void A_candle_without_trades_is_flat_at_the_close_before_it_with_no_volume()
    {
        IdeaCandles.FromMinutes(Interval.FiveMinutes, IdeaFixtures.Minutes(), Ten.AddMinutes(15), Ten.AddMinutes(30)).ShouldBe(
        [
            new IdeaCandle(Ten.AddMinutes(15), 49.25m, 49.33m, 48.39m, 48.71m, 1991m),
            new IdeaCandle(Ten.AddMinutes(20), 48.71m, 48.71m, 48.71m, 48.71m, 0m),
            new IdeaCandle(Ten.AddMinutes(25), 49.61m, 50.16m, 48.92m, 49.10m, 1911m)
        ]);
    }

    // Candles follow the grid of the interval in UTC: the hour of 10:00 is not whole after 10:30.
    [Test]
    public void Only_candles_of_the_grid_that_lie_whole_in_the_period_count()
    {
        IdeaCandles.FromMinutes(Interval.OneHour, IdeaFixtures.Minutes(), Ten.AddMinutes(30), Ten.AddHours(2))
            .Select(candle => candle.OpenTime)
            .ShouldBe([Ten.AddHours(1)]);
    }

    [Test]
    public void Without_a_minute_before_a_gap_the_candle_opens_at_its_first_minute()
    {
        IdeaCandle[] minutes = [.. IdeaFixtures.Minutes().Where(minute => minute.OpenTime >= Ten.AddMinutes(3))];

        IdeaCandles.FromMinutes(Interval.OneHour, minutes, Ten, Ten.AddHours(1)).ShouldBe([new IdeaCandle(Ten, 50.67m, 50.70m, 47.63m, 48.27m, 19609m)]);
    }

    [Test]
    public void A_candle_without_trades_and_without_a_close_before_is_left_out()
    {
        IdeaCandle[] minutes = [.. IdeaFixtures.Minutes().Where(minute => minute.OpenTime >= Ten.AddMinutes(6))];

        IdeaCandles.FromMinutes(Interval.FiveMinutes, minutes, Ten, Ten.AddMinutes(10)).Select(candle => candle.OpenTime).ShouldBe([Ten.AddMinutes(5)]);
    }

    // A minute of the covered period without a candle had no trades: the engine of ideas takes it flat at the close before it.
    [Test]
    public void Minutes_of_a_period_fill_the_ones_without_trades()
    {
        IdeaCandles.Minutes(IdeaFixtures.Minutes(), Ten.AddMinutes(19), Ten.AddMinutes(26), 0m).ShouldBe(
        [
            IdeaFixtures.Minutes().Single(minute => minute.OpenTime == Ten.AddMinutes(19)),
            new IdeaCandle(Ten.AddMinutes(20), 48.71m, 48.71m, 48.71m, 48.71m, 0m),
            new IdeaCandle(Ten.AddMinutes(21), 48.71m, 48.71m, 48.71m, 48.71m, 0m),
            new IdeaCandle(Ten.AddMinutes(22), 48.71m, 48.71m, 48.71m, 48.71m, 0m),
            new IdeaCandle(Ten.AddMinutes(23), 48.71m, 48.71m, 48.71m, 48.71m, 0m),
            new IdeaCandle(Ten.AddMinutes(24), 48.71m, 48.71m, 48.71m, 48.71m, 0m),
            IdeaFixtures.Minutes().Single(minute => minute.OpenTime == Ten.AddMinutes(25))
        ]);
    }

    // Without a close before, a gap at the start of the period is no flat minute: the minutes start at the first one known.
    [Test]
    public void Without_a_close_before_the_minutes_start_at_the_first_one_known()
    {
        IdeaCandles.Minutes(IdeaFixtures.Minutes(), Ten.AddMinutes(20), Ten.AddMinutes(26), null).Select(minute => minute.OpenTime).ShouldBe([Ten.AddMinutes(25)]);
    }

    [Test]
    public void A_gap_at_the_start_of_the_period_takes_the_close_before_it()
    {
        IdeaCandles.Minutes(IdeaFixtures.Minutes(), Ten.AddMinutes(20), Ten.AddMinutes(21), 48.71m).ShouldBe([new IdeaCandle(Ten.AddMinutes(20), 48.71m, 48.71m, 48.71m, 48.71m, 0m)]);
    }
}