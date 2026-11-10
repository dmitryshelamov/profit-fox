using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.TradeFlow;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>Requirement "Запись закрытой минуты" and decision 6 of collect-trade-flow.</summary>
public class TradeFlowMinutesTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private FakeTimeProvider _time = null!;
    private TradeFlowMinutes _minutes = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Minute.AddSeconds(10));
        _minutes = new TradeFlowMinutes(_time);
    }

    // Scenario "Минута закрылась": 5 seconds after its end the minute of every pair goes in one batch and is not given again.
    [Test]
    public void Minute_is_given_5_seconds_after_its_end_with_every_pair_once()
    {
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m));
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(2), TradeSide.Sell, 3m, 149m));
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(3), TradeSide.Buy, 1m, 70000m));
        _time.SetUtcNow(Minute.AddSeconds(64.999));
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(64), TradeSide.Buy, 1m, 151m));
        IReadOnlyList<(Instrument, TradeFlowMinute)> early = _minutes.TakeClosed();

        _time.SetUtcNow(Minute.AddSeconds(65));
        IReadOnlyList<(Instrument, TradeFlowMinute)> closed = _minutes.TakeClosed();
        IReadOnlyList<(Instrument, TradeFlowMinute)> again = _minutes.TakeClosed();

        early.ShouldBeEmpty();
        closed.ShouldBe(
        [
            (Sol, new TradeFlowMinute(Minute, 2m, 3m, 300m, 447m, 1, 1)),
            (Btc, new TradeFlowMinute(Minute, 1m, 0m, 70000m, 0m, 1, 0))
        ],
            ignoreOrder: true);
        again.ShouldBeEmpty();
    }

    [Test]
    public void Minute_that_has_not_ended_waits()
    {
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(30));

        _minutes.TakeClosed().ShouldBeEmpty();
    }

    // Decision 6: a trade of a written minute that is up to 2 minutes old writes the minute again, with every trade of it.
    [Test]
    public void Late_trade_gives_its_minute_again_whole()
    {
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed();

        _time.SetUtcNow(Minute.AddSeconds(179));
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(59), TradeSide.Sell, 1m, 149m));

        _minutes.TakeClosed().ShouldBe([(Sol, new TradeFlowMinute(Minute, 2m, 1m, 300m, 149m, 1, 1))]);
    }

    // Decision 6: a later trade is dropped; the rebuild of the day brings it back.
    [Test]
    public void Very_late_trade_is_dropped()
    {
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed();

        _time.SetUtcNow(Minute.AddSeconds(180));
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(59), TradeSide.Sell, 1m, 149m));
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(59), TradeSide.Sell, 1m, 70000m));

        _minutes.TakeClosed().ShouldBeEmpty();
    }

    // Requirement "Запись закрытой минуты": memory keeps the minutes still open and the written ones a late trade may still change, no more.
    [Test]
    public void Written_minute_is_forgotten_once_a_late_trade_can_no_longer_change_it()
    {
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(62), TradeSide.Buy, 2m, 150m));
        _minutes.TakeClosed();
        int written = _minutes.Kept;

        _time.SetUtcNow(Minute.AddSeconds(180));
        _minutes.TakeClosed();

        written.ShouldBe(2);
        _minutes.Kept.ShouldBe(1);
    }
}