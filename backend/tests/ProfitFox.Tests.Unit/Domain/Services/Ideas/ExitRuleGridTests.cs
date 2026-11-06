using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Допустимые значения правила": the grid of lifetimes and entry windows of an interval, and a time on it.</summary>
public class ExitRuleGridTests
{
    // Scenario "Сетка на 4 ч": 7 days are 42 candles of 4 hours.
    [Test]
    public void Grid_of_four_hours_ends_at_seven_days()
    {
        ExitRuleGrid.Of(Interval.FourHours).ShouldBe([1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 42]);
    }

    [Test]
    public void Grid_of_five_minutes_ends_at_seven_days()
    {
        IReadOnlyList<int> grid = ExitRuleGrid.Of(Interval.FiveMinutes);

        grid.TakeLast(2).ShouldBe([1536, 2016]);
        grid.Count.ShouldBe(22);
    }

    // 7 days on the grid itself: 168 hours, between 128 and 192.
    [Test]
    public void Grid_of_an_hour_ends_at_seven_days()
    {
        ExitRuleGrid.Of(Interval.OneHour).TakeLast(3).ShouldBe([96, 128, 168]);
    }

    // Scenario "Время в свечи": 10 hours are 40 candles of 15 minutes, 48 on the grid.
    [Test]
    public void Time_goes_up_to_the_grid()
    {
        ExitRuleGrid.Candles(Interval.FifteenMinutes, TimeSpan.FromHours(10)).ShouldBe(48);
        ExitRuleGrid.Candles(Interval.FifteenMinutes, TimeSpan.FromHours(12)).ShouldBe(48);
        ExitRuleGrid.Candles(Interval.FifteenMinutes, TimeSpan.FromMinutes(20)).ShouldBe(2);
        ExitRuleGrid.Candles(Interval.FourHours, TimeSpan.FromDays(6)).ShouldBe(42);
    }
}