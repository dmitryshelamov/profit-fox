using Bybit.Net.Enums;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitCodesTests
{
    [TestCase(MarketCategory.Spot, Category.Spot)]
    [TestCase(MarketCategory.Linear, Category.Linear)]
    public void Category_maps_to_bybit(MarketCategory category, Category expected)
    {
        category.ToBybit().ShouldBe(expected);
    }

    [TestCase(Interval.OneMinute, KlineInterval.OneMinute)]
    [TestCase(Interval.ThreeMinutes, KlineInterval.ThreeMinutes)]
    [TestCase(Interval.FiveMinutes, KlineInterval.FiveMinutes)]
    [TestCase(Interval.FifteenMinutes, KlineInterval.FifteenMinutes)]
    [TestCase(Interval.ThirtyMinutes, KlineInterval.ThirtyMinutes)]
    [TestCase(Interval.OneHour, KlineInterval.OneHour)]
    [TestCase(Interval.TwoHours, KlineInterval.TwoHours)]
    [TestCase(Interval.FourHours, KlineInterval.FourHours)]
    [TestCase(Interval.SixHours, KlineInterval.SixHours)]
    [TestCase(Interval.TwelveHours, KlineInterval.TwelveHours)]
    [TestCase(Interval.OneDay, KlineInterval.OneDay)]
    [TestCase(Interval.OneWeek, KlineInterval.OneWeek)]
    [TestCase(Interval.OneMonth, KlineInterval.OneMonth)]
    public void Interval_maps_to_bybit(Interval interval, KlineInterval expected)
    {
        interval.ToBybit().ShouldBe(expected);
    }
}