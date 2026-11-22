using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Signals;

/// <summary>
///     Spec signal-preview «Те же события, что у стенда» (task 1.2): the events of a pair that both a backtest and the
///     preview use, on the H0001 signal (funding rate first at or above 0.0005).
/// </summary>
public sealed class PairEventsTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly Signal H0001 = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

    [Test]
    public void A_candle_without_trades_is_not_given_to_the_signal()
    {
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0)), Untraded(Utc(2026, 1, 5, 4)), Traded(Utc(2026, 1, 5, 8))];
        HistoryPayment[] funding = [Paid(Utc(2026, 1, 5, 0), "0.0001"), Paid(Utc(2026, 1, 5, 6), "0.001")];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(H0001, FourHours, "BTCUSDT", candles, funding);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 1, 5, 12) }), "the high rate is first seen at the close of the next traded candle");
    }

    [Test]
    public void A_payment_at_the_close_is_seen_by_that_candle()
    {
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0)), Traded(Utc(2026, 1, 5, 4))];
        HistoryPayment[] funding = [Paid(Utc(2026, 1, 5, 0), "0.0001"), Paid(Utc(2026, 1, 5, 8), "0.001")];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(H0001, FourHours, "BTCUSDT", candles, funding);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 1, 5, 8) }));
    }

    [Test]
    public void An_event_whose_trade_leaves_the_period_is_still_returned()
    {
        HistoryCandle[] candles = [Traded(Utc(2026, 5, 15, 16)), Traded(Utc(2026, 5, 15, 20))];
        HistoryPayment[] funding = [Paid(Utc(2026, 5, 15, 16), "0.0001"), Paid(Utc(2026, 5, 16), "0.001")];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(H0001, FourHours, "BTCUSDT", candles, funding);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 5, 16) }));
        Assert.That(Exploration().Fits(Utc(2026, 5, 16), FourHours), Is.False, "a backtest of H0001 up to 2026-05-16 leaves this order out");
    }

    private static HistoryCandle Traded(DateTimeOffset open)
    {
        return new HistoryCandle(open, "100", "101", "99", "100.5", "12.5", Turnover: null, BuyVolume: null);
    }

    private static HistoryCandle Untraded(DateTimeOffset open)
    {
        return new HistoryCandle(open, "100", "100", "100", "100", "0", Turnover: null, BuyVolume: null);
    }

    private static HistoryPayment Paid(DateTimeOffset time, string rate)
    {
        return new HistoryPayment(time, rate);
    }
}
