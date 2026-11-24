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

    /// <summary>
    ///     Task 27.3 (engine version 4): before it the candle closed at 8:00 saw the payment of 8:00 and gave the event at 8:00.
    /// </summary>
    [Test]
    public void A_payment_at_the_close_is_seen_by_the_next_candle()
    {
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0)), Traded(Utc(2026, 1, 5, 4)), Traded(Utc(2026, 1, 5, 8))];
        HistoryPayment[] funding = [Paid(Utc(2026, 1, 5, 0), "0.0001"), Paid(Utc(2026, 1, 5, 8), "0.001")];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(H0001, FourHours, "BTCUSDT", candles, funding);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 1, 5, 12) }));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 27.3 (engine version 4): a payment at <c>T</c> belongs to the candle that closes
    ///     after <c>T</c>, in a backtest as live, where the payment comes a minute or three after its time. The payment of 8:00
    ///     gives the event of <c>funding-rate</c> on the candle of <c>5m</c> closed at 8:05 and on that of <c>1h</c> closed at
    ///     9:00, not on the candles closed at 8:00.
    /// </summary>
    [Test]
    public void A_payment_belongs_to_the_candle_that_closes_after_it()
    {
        HistoryPayment[] funding = [Paid(Utc(2026, 1, 5, 0), "0.0001"), Paid(Utc(2026, 1, 5, 8), "0.001")];
        HistoryCandle[] fives = [.. Enumerable.Range(0, 4).Select(index => Traded(Utc(2026, 1, 5, 7).AddMinutes(50 + (5 * index))))];
        HistoryCandle[] hours = [Traded(Utc(2026, 1, 5, 7)), Traded(Utc(2026, 1, 5, 8))];

        IReadOnlyList<DateTimeOffset> onFive = PairEvents.Of(H0001, TimeSpan.FromMinutes(5), "BTCUSDT", fives, funding);
        IReadOnlyList<DateTimeOffset> onHour = PairEvents.Of(H0001, TimeSpan.FromHours(1), "BTCUSDT", hours, funding);

        Assert.That(onFive, Is.EqualTo(new[] { Utc(2026, 1, 5, 8).AddMinutes(5) }));
        Assert.That(onHour, Is.EqualTo(new[] { Utc(2026, 1, 5, 9) }));
    }

    [Test]
    public void An_event_whose_trade_leaves_the_period_is_still_returned()
    {
        HistoryCandle[] candles = [Traded(Utc(2026, 5, 15, 16)), Traded(Utc(2026, 5, 15, 20))];
        HistoryPayment[] funding = [Paid(Utc(2026, 5, 15, 16), "0.0001"), Paid(Utc(2026, 5, 15, 20), "0.001")];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(H0001, FourHours, "BTCUSDT", candles, funding);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 5, 16) }));
        Assert.That(Exploration().Fits(Utc(2026, 5, 16), FourHours), Is.False, "a backtest of H0001 up to 2026-05-16 leaves this order out");
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 3.4: spec signal-preview «Состояние держится три свечи».
    /// </summary>
    [Test]
    public void A_state_held_for_three_candles_is_one_event_at_the_close_of_the_first()
    {
        Signal state = StrategyGraph.ReadSignal(Graphs.CloseAbove).Value!;
        decimal[] closes = [99, 100, 101, 102, 99];
        HistoryCandle[] candles = [.. closes.Select((close, index) => Closing(Utc(2026, 1, 5, 0).AddHours(4 * index), close))];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(state, FourHours, "BTCUSDT", candles, []);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 1, 5, 8) }));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 3.4: spec signal-preview «Состояние вернулось».
    /// </summary>
    [Test]
    public void A_state_that_comes_back_is_a_second_event()
    {
        Signal state = StrategyGraph.ReadSignal(Graphs.CloseAbove).Value!;
        decimal[] closes = [101, 99, 101];
        HistoryCandle[] candles = [.. closes.Select((close, index) => Closing(Utc(2026, 1, 5, 0).AddHours(4 * index), close))];

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(state, FourHours, "BTCUSDT", candles, []);

        Assert.That(events, Is.EqualTo(new[] { Utc(2026, 1, 5, 4), Utc(2026, 1, 5, 12) }));
    }

    private static HistoryCandle Closing(DateTimeOffset open, decimal close)
    {
        string text = close.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new HistoryCandle(open, text, text, text, text, "12.5", Turnover: null, BuyVolume: null);
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
