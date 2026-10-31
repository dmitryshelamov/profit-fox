using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.MarketData.Enums;

public class IntervalMissingRangesTests
{
    [Test]
    public void Whole_period_is_missing_without_stored_candles()
    {
        Interval.OneHour.MissingRanges([], At("2024-01-01T00:00:00Z"), At("2024-01-01T03:00:00Z"))
            .ShouldBe([(At("2024-01-01T00:00:00Z"), At("2024-01-01T03:00:00Z"))]);
    }

    [Test]
    public void Nothing_is_missing_when_every_candle_is_stored()
    {
        DateTimeOffset[] stored = [At("2024-01-01T00:00:00Z"), At("2024-01-01T01:00:00Z"), At("2024-01-01T02:00:00Z")];

        Interval.OneHour.MissingRanges(stored, At("2024-01-01T00:00:00Z"), At("2024-01-01T03:00:00Z")).ShouldBeEmpty();
    }

    [Test]
    public void Gaps_inside_the_period_are_missing()
    {
        DateTimeOffset[] stored = [At("2024-01-01T00:00:00Z"), At("2024-01-01T02:00:00Z"), At("2024-01-01T05:00:00Z")];

        Interval.OneHour.MissingRanges(stored, At("2024-01-01T00:00:00Z"), At("2024-01-01T06:00:00Z"))
            .ShouldBe([(At("2024-01-01T01:00:00Z"), At("2024-01-01T02:00:00Z")), (At("2024-01-01T03:00:00Z"), At("2024-01-01T05:00:00Z"))]);
    }

    [Test]
    public void Head_before_the_first_and_tail_after_the_last_stored_candle_are_missing()
    {
        DateTimeOffset[] stored = [At("2024-01-01T02:00:00Z"), At("2024-01-01T03:00:00Z")];

        Interval.OneHour.MissingRanges(stored, At("2024-01-01T00:00:00Z"), At("2024-01-01T06:00:00Z"))
            .ShouldBe([(At("2024-01-01T00:00:00Z"), At("2024-01-01T02:00:00Z")), (At("2024-01-01T04:00:00Z"), At("2024-01-01T06:00:00Z"))]);
    }

    // The head holds no candle of the grid, so the exchange answers it with an empty page.
    [Test]
    public void Head_from_a_start_off_the_grid_is_missing()
    {
        DateTimeOffset[] stored = [At("2024-01-01T01:00:00Z")];

        Interval.OneHour.MissingRanges(stored, At("2024-01-01T00:30:00Z"), At("2024-01-01T02:00:00Z"))
            .ShouldBe([(At("2024-01-01T00:30:00Z"), At("2024-01-01T01:00:00Z"))]);
    }

    // The tail holds only the candle that is not closed yet.
    [Test]
    public void Tail_up_to_an_end_off_the_grid_is_missing()
    {
        DateTimeOffset[] stored = [At("2024-03-10T10:00:00Z"), At("2024-03-10T11:00:00Z")];

        Interval.OneHour.MissingRanges(stored, At("2024-03-10T10:00:00Z"), At("2024-03-10T12:30:00Z"))
            .ShouldBe([(At("2024-03-10T12:00:00Z"), At("2024-03-10T12:30:00Z"))]);
    }

    [Test]
    public void Weekly_candles_step_by_seven_days()
    {
        DateTimeOffset[] stored = [At("2024-01-01T00:00:00Z"), At("2024-01-15T00:00:00Z")];

        Interval.OneWeek.MissingRanges(stored, At("2024-01-01T00:00:00Z"), At("2024-01-22T00:00:00Z"))
            .ShouldBe([(At("2024-01-08T00:00:00Z"), At("2024-01-15T00:00:00Z"))]);
    }

    // Scenario "Календарный интервал".
    [Test]
    public void Monthly_candles_step_by_calendar_month()
    {
        DateTimeOffset[] stored = [At("2024-01-01T00:00:00Z"), At("2024-03-01T00:00:00Z")];

        Interval.OneMonth.MissingRanges(stored, At("2024-01-01T00:00:00Z"), At("2024-04-01T00:00:00Z"))
            .ShouldBe([(At("2024-02-01T00:00:00Z"), At("2024-03-01T00:00:00Z"))]);
    }

    private static DateTimeOffset At(string time)
    {
        return DateTimeOffset.Parse(time);
    }
}