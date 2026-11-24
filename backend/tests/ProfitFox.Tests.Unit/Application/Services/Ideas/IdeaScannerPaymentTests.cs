using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 25.3: the funding payment at the close of a candle. The warm-up and a backtest put a
///     payment on the candle that closes at its time, and the live feed brings it after the close and its delay: the live
///     close of the candle at the time of a payment of the pair waits for it in the book, no longer than
///     <see cref="ScannerSettings.PaymentWait" />. The pair <c>BTCUSDT</c> pays every four hours, at 3:00 and 7:00 at the
///     rate 0.0001, and at 11:00 at 0.001: <c>funding-short</c> enters short the first time the rate is above 0.0005.
/// </summary>
public sealed class IdeaScannerPaymentTests
{
    private static readonly DateTimeOffset Eleven = Ten.AddHours(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    /// <summary>
    ///     Task 25.3 (а), confirmed: the payment of 11:00 comes 30 s after the close; the live scanner saw it only on the next
    ///     candle and gave the idea of 11:05, a backtest gives that of 11:00. The candle of BTCUSDT waits for its payment; the
    ///     other pairs and the strategy without funding do not: the idea of <c>close-up</c> on ETHUSDT is there at 11:00:15.
    /// </summary>
    [Test]
    public async Task A_payment_that_comes_after_the_close_gives_the_idea_on_the_candle_closed_at_its_time()
    {
        ScannerRig rig = await FundingRig(FiveMinutes, closeUp: true);
        await rig.Minutes("BTCUSDT", Ten, 60, close: 100);
        await rig.Minutes("ETHUSDT", Ten, 55, close: 99);
        await rig.Minutes("ETHUSDT", Ten.AddMinutes(55), 5, close: 101);

        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.StrategyName, idea.Symbol, idea.Moment)), Is.EqualTo(new[] { ("close-up", "ETHUSDT", Eleven) }), "BTCUSDT waits for its payment of 11:00");

        await Pay(rig, "BTCUSDT", Eleven, 0.001m);
        await rig.At(Eleven.AddSeconds(45));

        Assert.That(
            rig.Ideas.Ideas.Where(idea => idea.StrategyName == "funding-short").Select(idea => (idea.Symbol, idea.Moment, idea.Side)),
            Is.EqualTo(new[] { ("BTCUSDT", Eleven, EntrySide.Short) }),
            "the moment of a backtest");
        Assert.That(rig.Log.Entries.Where(entry => entry.Level >= LogLevel.Warning), Is.Empty);
    }

    /// <summary>
    ///     Task 25.3: a payment that does not come within the limit does not hold the candle for ever — it is computed without
    ///     the payment, and the journal names the pair and the time of the payment. The payment that comes later is seen on the
    ///     next candle, as before.
    /// </summary>
    [Test]
    public async Task A_payment_that_does_not_come_within_the_limit_leaves_the_candle_without_it_with_a_warning()
    {
        ScannerRig rig = await FundingRig(FiveMinutes);
        await rig.Minutes("BTCUSDT", Ten, 60, close: 100);
        await rig.At(Eleven.AddSeconds(15));
        await rig.At(Eleven.AddSeconds(74));
        Assert.That(rig.Log.Entries, Is.Empty, "within the limit");

        await rig.At(Eleven.AddSeconds(75));
        (LogLevel Level, string Message) warning = rig.Log.Entries.Single();
        Assert.That(warning.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(warning.Message, Does.Contain("BTCUSDT").And.Contain("2026-11-22T11:00:00"));
        Assert.That(rig.Ideas.Ideas, Is.Empty, "the candle of 11:00 is computed without the payment");

        await Pay(rig, "BTCUSDT", Eleven, 0.001m);
        await rig.Minutes("BTCUSDT", Eleven, 5, close: 100);
        await rig.At(Eleven.AddMinutes(5).AddSeconds(15));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Moment), Is.EqualTo(new[] { Eleven.AddMinutes(5) }));
        Assert.That(rig.Log.Entries, Has.Count.EqualTo(1), "the next candle has the payment and does not wait");
    }

    /// <summary>
    ///     Task 25.3 (б), confirmed: the Worker restarted between the payment of 11:00 and the close of 12:00. The warm-up put
    ///     the payment on the candle of 11:00 — the rate above the level there already — so the live close of 12:00 had no
    ///     first time, and the live close of 11:00, computed without the payment, had none either: no idea at all. With the
    ///     wait the idea of 11:00 is written before the restart, and a second one is not born after it.
    /// </summary>
    [Test]
    public async Task A_restart_between_the_payment_and_the_next_close_gives_the_idea_once()
    {
        ScannerRig before = await FundingRig(OneHour);
        await before.Minutes("BTCUSDT", Ten, 60, close: 100);
        await before.At(Eleven.AddSeconds(15));
        await Pay(before, "BTCUSDT", Eleven, 0.001m);
        await before.At(Eleven.AddSeconds(30));

        var after = new ScannerRig();
        after.Time.SetUtcNow(Eleven.AddMinutes(32));
        after.History.Payments["btc"] = [.. Payments, new HistoryPayment(Eleven, "0.001")];
        after.Ideas.Ideas.AddRange(before.Ideas.Ideas);
        await after.EnableFunding("funding-short", "30m", id: before.Strategies.Documents.Single().Id, timeframes: [OneHour]);
        await after.Start();
        await Pay(after, "BTCUSDT", Ten.AddHours(-7), 0.0001m);
        await Pay(after, "BTCUSDT", Ten.AddHours(-3), 0.0001m);
        await Pay(after, "BTCUSDT", Eleven, 0.001m);
        await after.Minutes("BTCUSDT", Eleven, 60, close: 100);
        await after.At(Eleven.AddHours(1).AddSeconds(15));

        Assert.That(after.Ideas.Ideas.Select(idea => (idea.Symbol, idea.Moment)), Is.EqualTo(new[] { ("BTCUSDT", Eleven) }));
    }

    /// <summary>
    ///     Task 25.3 with 25.1 and 26.2: after a new subscription the feed replays the minutes up to 11:06, and the boundaries of
    ///     11:00 and 11:05 close in one pass of the catch-up. The candle of BTCUSDT at 11:00 waits for its payment and the one
    ///     of 11:05 waits behind it; once the payment came, both are computed in their order, each once.
    /// </summary>
    [Test]
    public async Task A_candle_of_a_catch_up_waits_for_its_payment_and_the_next_candle_of_the_pair_waits_behind_it()
    {
        ScannerRig rig = await FundingRig(FiveMinutes, closeUp: true);
        for (int minute = 0; minute < 53; minute++)
        {
            await rig.Minutes("BTCUSDT", Ten.AddMinutes(minute), 1, close: 100);
            await rig.At(Ten.AddMinutes(minute + 1).AddSeconds(1));
        }

        rig.Time.SetUtcNow(Eleven.AddMinutes(6).AddSeconds(30));
        await rig.Scanner.ApplyAsync(new FeedResubscribed("minutes", Ten.AddMinutes(48)), CancellationToken.None);
        for (int minute = 48; minute < 67; minute++)
        {
            await rig.Minutes("BTCUSDT", Ten.AddMinutes(minute), 1, close: minute is >= 60 and < 65 ? 101 : 100);
            await rig.Minutes("ETHUSDT", Ten.AddMinutes(minute), 1, close: 101);
        }

        await rig.At(Eleven.AddMinutes(6).AddSeconds(31), quiet: false);
        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.StrategyName, idea.Symbol, idea.Moment)), Is.EquivalentTo(new[] { ("close-up", "ETHUSDT", Ten.AddMinutes(55)) }), "BTCUSDT waits at 11:00 and 11:05");

        await Pay(rig, "BTCUSDT", Eleven, 0.001m);
        await rig.At(Eleven.AddMinutes(6).AddSeconds(32), quiet: false);
        Assert.That(
            rig.Ideas.Ideas.Where(idea => idea.Symbol == "BTCUSDT").Select(idea => (idea.StrategyName, idea.Moment)),
            Is.EquivalentTo(new[] { ("funding-short", Eleven), ("close-up", Eleven.AddMinutes(5)) }),
            "the candle of 11:05 closes at 101 for the first time, after that of 11:00");
        Assert.That(rig.Log.Entries.Where(entry => entry.Level >= LogLevel.Warning), Is.Empty);
    }

    /// <summary>
    ///     Task 25.3: the ideas of the pairs that waited for their payments are one candle with each other — the payments come
    ///     some seconds apart, and «⚡ всплеск» counts the pairs of the whole candle, not those of one payment.
    /// </summary>
    [Test]
    public async Task The_ideas_of_the_pairs_that_waited_for_their_payments_make_one_burst()
    {
        Instrument[] pairs = Many(10);
        ScannerRig rig = await FundingRig(OneHour, pairs: pairs);
        foreach (Instrument pair in pairs)
        {
            await rig.Minutes(pair.Symbol, Ten, 60, close: 100);
        }

        await rig.At(Eleven.AddSeconds(15));
        foreach (Instrument pair in pairs.Take(5))
        {
            await Pay(rig, pair.Symbol, Eleven, 0.001m);
        }

        await rig.At(Eleven.AddSeconds(20));
        Assert.That(rig.Ideas.Ideas, Is.Empty, "the candle is not over while pairs wait for their payments");
        foreach (Instrument pair in pairs.Skip(5))
        {
            await Pay(rig, pair.Symbol, Eleven, 0.001m);
        }

        await rig.At(Eleven.AddSeconds(25));
        Assert.That(rig.Ideas.Ideas, Has.Count.EqualTo(10));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Labels), Has.All.Contains(IdeasSettings.BurstLabel), "ten shorts on 1h");
    }

    // The payments of BTCUSDT before 10:00 the history and the book have: every four hours at 0.0001.
    private static readonly HistoryPayment[] Payments = [new HistoryPayment(Ten.AddHours(-7), "0.0001"), new HistoryPayment(Ten.AddHours(-3), "0.0001")];

    // The scanner at 10:00:05 with funding-short on the timeframe (and close-up when asked) warmed up, the payments of 3:00
    // and 7:00 of every pair that has them in its book as the feed of payments caught them up, and the feed live.
    private static async Task<ScannerRig> FundingRig(TimeSpan timeframe, bool closeUp = false, Instrument[]? pairs = null)
    {
        var rig = new ScannerRig(pairs);
        Instrument[] paying = pairs ?? [Pairs[0]];
        foreach (Instrument pair in paying)
        {
            rig.History.Payments[pair.Id] = [.. Payments];
        }

        await rig.EnableFunding("funding-short", "30m", timeframes: [timeframe]);
        if (closeUp)
        {
            await rig.Enable("close-up", "8h", timeframes: [timeframe]);
        }

        await rig.Start();
        foreach (Instrument pair in paying)
        {
            await Pay(rig, pair.Symbol, Ten.AddHours(-7), 0.0001m);
            await Pay(rig, pair.Symbol, Ten.AddHours(-3), 0.0001m);
        }

        return rig;
    }

    private static Task Pay(ScannerRig rig, string symbol, DateTimeOffset time, decimal rate)
    {
        return rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.FundingPayment, symbol, time, rate), CancellationToken.None);
    }
}
