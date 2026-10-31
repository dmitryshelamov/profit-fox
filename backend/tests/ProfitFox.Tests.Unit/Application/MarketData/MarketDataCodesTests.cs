using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class MarketDataCodesTests
{
    [TestCase("spot", MarketCategory.Spot)]
    [TestCase("linear", MarketCategory.Linear)]
    public void Category_code_maps_both_ways(string code, MarketCategory category)
    {
        MarketDataCodes.TryParseCategory(code, out var parsed).ShouldBeTrue();
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
        MarketDataCodes.TryParseInterval(code, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(interval);
        interval.ToCode().ShouldBe(code);
    }

    [Test]
    public void Unsupported_interval_is_rejected()
    {
        MarketDataCodes.TryParseInterval("2", out _).ShouldBeFalse();
    }
}
