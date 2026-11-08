using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 9: the interval of a subscription to the candles of a series.</summary>
public class TInvestCodesTests
{
    [TestCase(Interval.OneMinute, SubscriptionInterval.OneMinute)]
    [TestCase(Interval.ThreeMinutes, SubscriptionInterval._3Min)]
    [TestCase(Interval.FiveMinutes, SubscriptionInterval.FiveMinutes)]
    [TestCase(Interval.FifteenMinutes, SubscriptionInterval.FifteenMinutes)]
    [TestCase(Interval.ThirtyMinutes, SubscriptionInterval._30Min)]
    [TestCase(Interval.OneHour, SubscriptionInterval.OneHour)]
    [TestCase(Interval.TwoHours, SubscriptionInterval._2Hour)]
    [TestCase(Interval.FourHours, SubscriptionInterval._4Hour)]
    public void Interval_of_moex_has_its_subscription(Interval interval, SubscriptionInterval subscription)
    {
        interval.ToSubscription().ShouldBe(subscription);
    }

    [Test]
    public void Interval_t_invest_has_no_candles_of_has_no_subscription()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Interval.OneDay.ToSubscription());
    }
}