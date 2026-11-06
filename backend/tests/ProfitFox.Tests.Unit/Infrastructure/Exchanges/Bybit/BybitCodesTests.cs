using Bybit.Net.Enums;

using ProfitFox.Application.Ports.LiveTrading;

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

    // The Partial mode names the orders it makes PartialTakeProfit and PartialStopLoss.
    [TestCase(StopOrderType.TakeProfit, StopOrderKind.TakeProfit)]
    [TestCase(StopOrderType.PartialTakeProfit, StopOrderKind.TakeProfit)]
    [TestCase(StopOrderType.StopLoss, StopOrderKind.StopLoss)]
    [TestCase(StopOrderType.PartialStopLoss, StopOrderKind.StopLoss)]
    [TestCase(StopOrderType.TrailingStop, StopOrderKind.None)]
    [TestCase(null, StopOrderKind.None)]
    public void Stop_order_type_maps_to_the_kind_of_the_stop(StopOrderType? type, StopOrderKind expected)
    {
        type.ToStopKind().ShouldBe(expected);
    }
}