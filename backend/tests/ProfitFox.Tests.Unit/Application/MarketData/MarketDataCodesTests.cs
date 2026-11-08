using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class MarketDataCodesTests
{
    [TestCase("spot", MarketCategory.Spot)]
    [TestCase("linear", MarketCategory.Linear)]
    [TestCase("moex", MarketCategory.Moex)]
    public void Category_code_maps_both_ways(string code, MarketCategory category)
    {
        MarketDataCodes.TryParseCategory(code, out MarketCategory parsed).ShouldBeTrue();
        parsed.ShouldBe(category);
        category.ToCode().ShouldBe(code);
    }

    [Test]
    public void Inverse_category_is_rejected()
    {
        MarketDataCodes.TryParseCategory("inverse", out _).ShouldBeFalse();
    }

    [TestCase("1", Interval.OneMinute)]
    [TestCase("3", Interval.ThreeMinutes)]
    [TestCase("5", Interval.FiveMinutes)]
    [TestCase("15", Interval.FifteenMinutes)]
    [TestCase("30", Interval.ThirtyMinutes)]
    [TestCase("60", Interval.OneHour)]
    [TestCase("120", Interval.TwoHours)]
    [TestCase("240", Interval.FourHours)]
    [TestCase("360", Interval.SixHours)]
    [TestCase("720", Interval.TwelveHours)]
    [TestCase("D", Interval.OneDay)]
    [TestCase("W", Interval.OneWeek)]
    [TestCase("M", Interval.OneMonth)]
    public void Interval_code_maps_both_ways(string code, Interval interval)
    {
        MarketDataCodes.TryParseInterval(code, out Interval parsed).ShouldBeTrue();
        parsed.ShouldBe(interval);
        interval.ToCode().ShouldBe(code);
    }

    [Test]
    public void Unsupported_interval_is_rejected()
    {
        MarketDataCodes.TryParseInterval("2", out _).ShouldBeFalse();
    }

    // Requirement "Интервалы категории": Bybit has all 13 intervals, MOEX the eight up to four hours.
    [TestCase(MarketCategory.Spot)]
    [TestCase(MarketCategory.Linear)]
    public void Bybit_categories_have_every_interval(MarketCategory category)
    {
        MarketDataCodes.IntervalsOf(category).ShouldBe(Enum.GetValues<Interval>());
    }

    [Test]
    public void Moex_has_eight_intervals_without_days_weeks_months_and_half_days()
    {
        MarketDataCodes.IntervalsOf(MarketCategory.Moex).Select(interval => interval.ToCode()).ShouldBe(["1", "3", "5", "15", "30", "60", "120", "240"]);
    }

    [Test]
    public void Interval_error_lists_the_intervals_of_the_category()
    {
        MarketDataCodes.InvalidInterval(MarketCategory.Moex).ShouldBe("Interval must be one of the intervals of moex: 1, 3, 5, 15, 30, 60, 120, 240.");
        MarketDataCodes.InvalidInterval(MarketCategory.Spot).ShouldBe("Interval must be one of the intervals of spot: 1, 3, 5, 15, 30, 60, 120, 240, 360, 720, D, W, M.");
    }

    [Test]
    public void Category_error_lists_every_category()
    {
        MarketDataCodes.InvalidCategory.ShouldBe("Category must be spot, linear or moex.");
    }
}