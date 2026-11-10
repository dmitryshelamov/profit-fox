using System.Globalization;

using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.ValueObjects.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Market;

/// <summary>Requirement "Минутные суммы сделок": a trade goes to the minute of its time and to the side of its taker.</summary>
public class TradeFlowMinuteTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);

    // Scenario "Сделки одной минуты".
    [Test]
    public void Trades_of_a_minute_add_up_by_side()
    {
        TradeFlowMinute minute = TradeFlowMinute.Start(new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m))
            .Add(new TradeEvent(Minute.AddSeconds(2), TradeSide.Buy, 1m, 151m))
            .Add(new TradeEvent(Minute.AddSeconds(3), TradeSide.Sell, 3m, 149m));

        minute.ShouldBe(new TradeFlowMinute(Minute, 3m, 3m, 451m, 447m, 2, 1));
    }

    // Scenario "Сделка на границе минуты".
    [TestCase("2026-11-09T12:04:59.999Z", "2026-11-09T12:04:00Z")]
    [TestCase("2026-11-09T12:05:00.000Z", "2026-11-09T12:05:00Z")]
    public void Trade_goes_to_the_minute_of_its_time(string time, string openTime)
    {
        var minute = TradeFlowMinute.Start(new TradeEvent(DateTimeOffset.Parse(time, CultureInfo.InvariantCulture), TradeSide.Sell, 1m, 10m));

        minute.OpenTime.ShouldBe(DateTimeOffset.Parse(openTime, CultureInfo.InvariantCulture));
    }
}