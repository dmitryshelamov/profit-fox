using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.Market;

/// <summary>
///     Change add-ideas-and-market, task 5.5: spec market-live-feed «Свеча таймфрейма из минут», «Момент закрытия свечи»,
///     «Поздняя минута», «Догон после разрыва» («Свеча пришла дважды») and spec market-overview «Снимок рынка».
/// </summary>
public sealed class MarketBookTests
{
    private static readonly DateTimeOffset Ten = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Test]
    public void A_candle_of_5m_is_built_from_five_minutes()
    {
        MarketBook book = Book(Ten.AddMinutes(6));
        for (int minute = 0; minute < 5; minute++)
        {
            book.Apply(Minute("BTCUSDT", Ten.AddMinutes(minute), open: 100 + minute, close: 101 + minute, volume: 2));
        }

        ClosedCandle candle = book.Candle("BTCUSDT", FiveMinutes, Ten)!;

        Assert.That(candle, Is.EqualTo(new ClosedCandle(Ten, Ten.AddMinutes(5), 100, 106, 99, 105, 10)));
    }

    [Test]
    public void A_minute_without_trades_is_not_in_the_candle()
    {
        MarketBook book = Book(Ten.AddMinutes(6));
        foreach (int minute in new[] { 0, 1, 3, 4 })
        {
            book.Apply(Minute("BTCUSDT", Ten.AddMinutes(minute), open: 100, close: 100, volume: 3));
        }

        Assert.That(book.Candle("BTCUSDT", FiveMinutes, Ten)!.Volume, Is.EqualTo(12), "four minutes");
    }

    [Test]
    public void A_window_without_trades_has_no_candle()
    {
        MarketBook book = Book(Ten.AddMinutes(6));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(5), open: 100, close: 100));

        Assert.That(book.Candle("BTCUSDT", FiveMinutes, Ten), Is.Null);
        Assert.That(book.Candle("ETHUSDT", FiveMinutes, Ten), Is.Null);
    }

    [Test]
    public void A_minute_told_twice_is_one_minute_with_the_new_volume()
    {
        MarketBook book = Book(Ten.AddMinutes(6));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(5), open: 100, close: 100, volume: 1));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(5), open: 100, close: 100, volume: 7));

        Assert.That(book.Minutes("BTCUSDT", Ten, Ten.AddMinutes(10)).Select(minute => (minute.OpenTime, minute.Volume)), Is.EqualTo(new[] { (Ten.AddMinutes(5), 7m) }));
    }

    [Test]
    public void A_candle_is_closed_after_the_delay_once_the_feed_told_the_minute_before_its_close()
    {
        var time = new FakeTimeProvider(Ten.AddHours(2));
        var book = new MarketBook(time, TimeSpan.FromSeconds(15));
        DateTimeOffset noon = Ten.AddHours(2);
        book.Apply(Minute("BTCUSDT", noon.AddMinutes(-1), open: 1, close: 1));

        Assert.That(book.Closed(noon), Is.False, "12:00:00: the delay has not passed");
        time.SetUtcNow(noon.AddSeconds(15));
        Assert.That(book.Closed(noon), Is.True, "12:00:15 with the minute of 11:59");
    }

    [Test]
    public void A_candle_is_not_closed_while_the_feed_is_silent()
    {
        DateTimeOffset noon = Ten.AddHours(2);
        var time = new FakeTimeProvider(noon.AddMinutes(5));
        var book = new MarketBook(time, TimeSpan.FromSeconds(15));
        book.Apply(Minute("BTCUSDT", noon.AddMinutes(-3), open: 1, close: 1));

        Assert.That(book.Closed(noon), Is.False, "the feed broke off at 11:58");
        book.Apply(Minute("BTCUSDT", noon.AddMinutes(-1), open: 1, close: 1));
        Assert.That(book.Closed(noon), Is.True, "the feed came back and caught up");
    }

    /// <summary>
    ///     Task 25.1: the book is past a moment once the feed told a minute that opens at it or later.
    /// </summary>
    [Test]
    public void The_book_is_past_a_moment_once_a_minute_opens_at_it()
    {
        DateTimeOffset noon = Ten.AddHours(2);
        var book = new MarketBook(new FakeTimeProvider(noon.AddMinutes(10)), TimeSpan.FromSeconds(15));
        Assert.That(book.Past(noon), Is.False, "no minute yet");

        book.Apply(Minute("BTCUSDT", noon.AddMinutes(-1), open: 1, close: 1));
        Assert.That((book.Closed(noon), book.Past(noon)), Is.EqualTo((true, false)), "the minute of 11:59 of one pair");
        book.Apply(Minute("ETHUSDT", noon, open: 1, close: 1));
        Assert.That(book.Past(noon), Is.True, "the minute of 12:00");
    }

    [Test]
    public void A_late_minute_replaces_the_one_in_the_book()
    {
        MarketBook book = Book(Ten.AddMinutes(6));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(4), open: 100, close: 100));
        ClosedCandle before = book.Candle("BTCUSDT", FiveMinutes, Ten)!;

        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(4), open: 100, close: 98));

        Assert.That((before.Close, book.Candle("BTCUSDT", FiveMinutes, Ten)!.Close), Is.EqualTo((100m, 98m)));
    }

    [Test]
    public void The_change_over_an_hour_is_from_the_close_of_the_minute_an_hour_before()
    {
        MarketBook book = Book(Ten.AddHours(1).AddMinutes(1));
        book.Apply(Minute("SOLUSDT", Ten.AddMinutes(-1), open: 100, close: 100));
        book.Apply(Minute("SOLUSDT", Ten.AddHours(1).AddMinutes(-1), open: 101, close: 101));

        MarketPair pair = book.Snapshot("SOLUSDT")!;

        Assert.That((pair.Price, pair.Change1h), Is.EqualTo((101m, (decimal?)0.01m)));
    }

    [Test]
    public void Without_a_minute_an_hour_before_the_change_is_from_the_last_minute_closed_before_it()
    {
        MarketBook book = Book(Ten.AddHours(1).AddMinutes(1));
        book.Apply(Minute("SOLUSDT", Ten.AddMinutes(-3), open: 200, close: 200));
        book.Apply(Minute("SOLUSDT", Ten.AddHours(1).AddMinutes(-1), open: 210, close: 210));

        Assert.That(book.Snapshot("SOLUSDT")!.Change1h, Is.EqualTo(0.05m));
    }

    [Test]
    public void A_pair_of_six_hours_has_the_change_of_1h_and_4h_and_no_change_and_turnover_of_24h()
    {
        DateTimeOffset listed = Ten.AddHours(-6);
        MarketBook book = Book(Ten.AddMinutes(1));
        for (DateTimeOffset minute = listed; minute < Ten; minute = minute.AddMinutes(1))
        {
            book.Apply(Minute("NEWUSDT", minute, open: 10, close: 10, volume: 1));
        }

        MarketPair pair = book.Snapshot("NEWUSDT")!;

        Assert.That((pair.Change1h, pair.Change4h), Is.EqualTo(((decimal?)0m, (decimal?)0m)));
        Assert.That((pair.Change24h, pair.Turnover24h), Is.EqualTo(((decimal?)null, (decimal?)null)));
    }

    /// <summary>
    ///     Task 18.18: right after the start the book begins a minute or two after a day back; a book that begins no later
    ///     than five minutes after the moment gives the change and turnover of 24 hours from its first minute.
    /// </summary>
    [TestCase(2, true)]
    [TestCase(5, true)]
    [TestCase(6, false)]
    public void Right_after_the_start_the_day_is_counted_from_the_first_minute_of_the_book(int late, bool counted)
    {
        DateTimeOffset last = Ten.AddMinutes(-1);
        MarketBook book = Book(Ten.AddMinutes(1));
        book.Apply(Minute("BTCUSDT", last.AddDays(-1).AddMinutes(late), open: 100, close: 100, volume: 1));
        book.Apply(Minute("BTCUSDT", last, open: 100, close: 110, volume: 1));

        MarketPair pair = book.Snapshot("BTCUSDT")!;

        Assert.That((pair.Change24h, pair.Turnover24h), Is.EqualTo(counted ? ((decimal?)0.1m, (decimal?)210m) : ((decimal?)null, (decimal?)null)));
    }

    [Test]
    public void The_turnover_of_24h_sums_the_turnover_of_the_minutes_of_the_day()
    {
        MarketBook book = Book(Ten.AddMinutes(1));
        book.Apply(Minute("BTCUSDT", Ten.AddDays(-1).AddMinutes(-1), open: 100, close: 100, volume: 5));
        book.Apply(Minute("BTCUSDT", Ten.AddHours(-5), open: 100, close: 100, volume: 2));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(-1), open: 100, close: 110, volume: 3));

        MarketPair pair = book.Snapshot("BTCUSDT")!;

        Assert.That(pair.Turnover24h, Is.EqualTo(2 * 100m + 3 * 110m));
        Assert.That(pair.Change24h, Is.EqualTo(0.1m));
    }

    [Test]
    public void The_open_interest_in_USDT_is_the_coins_at_the_last_price()
    {
        MarketBook book = Book(Ten.AddMinutes(1));
        book.Apply(Minute("SOLUSDT", Ten.AddMinutes(-1), open: 238.42m, close: 238.42m));
        book.Apply(new LivePoint(LivePointKind.OpenInterest, "SOLUSDT", Ten.AddMinutes(-5), 24_000));
        book.Apply(new LivePoint(LivePointKind.FundingRate, "SOLUSDT", Ten.AddMinutes(-5), 0.000184m));

        MarketPair pair = book.Snapshot("SOLUSDT")!;

        Assert.That((pair.OpenInterest, pair.OpenInterestUsdt, pair.FundingRate), Is.EqualTo(((decimal?)24_000m, (decimal?)5_722_080m, (decimal?)0.000184m)));
    }

    [Test]
    public void The_next_payment_is_the_last_one_and_the_step_between_the_last_two()
    {
        MarketBook book = Book(Ten.AddHours(12));
        book.Apply(Minute("SOLUSDT", Ten.AddHours(12).AddMinutes(-1), open: 1, close: 1));
        book.Apply(new LivePoint(LivePointKind.FundingPayment, "SOLUSDT", Ten.AddHours(-2), 0.0001m));

        Assert.That(book.Snapshot("SOLUSDT")!.NextFunding, Is.Null, "one payment: no step");

        book.Apply(new LivePoint(LivePointKind.FundingPayment, "SOLUSDT", Ten.AddHours(6), 0.0002m));

        Assert.That(book.Snapshot("SOLUSDT")!.NextFunding, Is.EqualTo(Ten.AddHours(14)));
        // A payment at the close belongs to the next candle (task 27.3): the close at its time does not see it.
        Assert.That(book.LastPayment("SOLUSDT", Ten.AddHours(6).AddMinutes(5)), Is.EqualTo(new FundingPayment(Ten.AddHours(6), 0.0002m)));
        Assert.That(book.LastPayment("SOLUSDT", Ten.AddHours(6)), Is.EqualTo(new FundingPayment(Ten.AddHours(-2), 0.0001m)));
    }

    [Test]
    public void A_pair_that_left_the_set_is_no_longer_kept()
    {
        MarketBook book = Book(Ten.AddMinutes(1));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(-1), open: 1, close: 1));
        book.Apply(Minute("ETHUSDT", Ten.AddMinutes(-1), open: 1, close: 1));

        book.Apply(new MarketPairs([new Instrument("btc", "BTCUSDT")]));

        Assert.That(book.Symbols, Is.EqualTo(new[] { "BTCUSDT" }));
        Assert.That(book.Snapshot("ETHUSDT"), Is.Null);
    }

    [Test]
    public void A_minute_older_than_a_day_and_an_hour_before_the_newest_is_dropped()
    {
        MarketBook book = Book(Ten.AddMinutes(1));
        book.Apply(Minute("BTCUSDT", Ten.AddHours(-26), open: 1, close: 1));
        book.Apply(Minute("BTCUSDT", Ten.AddMinutes(-1), open: 1, close: 1));

        Assert.That(book.Minutes("BTCUSDT", Ten.AddDays(-2), Ten).Count(), Is.EqualTo(1));
    }

    private static MarketBook Book(DateTimeOffset now)
    {
        return new MarketBook(new FakeTimeProvider(now), TimeSpan.FromSeconds(15));
    }

    private static LiveMinute Minute(string symbol, DateTimeOffset at, decimal open, decimal close, decimal volume = 1)
    {
        return new LiveMinute(symbol, at, open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, volume, volume * close);
    }
}
