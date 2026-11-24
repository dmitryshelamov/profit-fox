using Microsoft.Extensions.Logging;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 27.3 (engine version 4): a funding payment at <c>T</c> belongs to the candle that
///     closes after <c>T</c>, in a backtest as live. The live close of a candle waits for no payment: the payment that comes a
///     minute or three after its time is in the book at the next close, where a backtest puts it. The pair <c>BTCUSDT</c>
///     pays every four hours, at 3:00 and 7:00 at the rate 0.0001, and at 11:00 at 0.001: <c>funding-short</c> enters short
///     for 30 minutes the first time the rate is above 0.0005.
/// </summary>
public sealed class IdeaScannerPaymentTests
{
    private static readonly DateTimeOffset Eleven = Ten.AddHours(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    /// <summary>
    ///     The data service gives a payment 59 to 177 s after its time (task 27.3). Whenever it comes within the candle, the
    ///     idea of <c>funding-short</c> is born at the close of 11:05 and is the trade of a backtest of the same strategy on
    ///     the same candles and minutes. At the close of 11:00 nobody waits: the ideas of <c>close-up</c> of both pairs are
    ///     written at once, BTCUSDT among them.
    /// </summary>
    [TestCase(30)]
    [TestCase(90)]
    [TestCase(170)]
    public async Task A_payment_that_comes_after_its_time_gives_the_idea_on_the_next_candle_as_a_backtest(int seconds)
    {
        ScannerRig rig = await FundingRig(FiveMinutes, closeUp: true);
        foreach (string symbol in new[] { "BTCUSDT", "ETHUSDT" })
        {
            await rig.Minutes(symbol, Ten, 55, close: 99);
            await rig.Minutes(symbol, Ten.AddMinutes(55), 5, close: 101);
        }

        await rig.At(Eleven.AddSeconds(15));
        Assert.That(
            rig.Ideas.Ideas.Select(idea => (idea.StrategyName, idea.Symbol, idea.Moment)),
            Is.EquivalentTo(new[] { ("close-up", "BTCUSDT", Eleven), ("close-up", "ETHUSDT", Eleven) }),
            "no candle waits for the payment of 11:00");

        await rig.At(Eleven.AddSeconds(seconds));
        await rig.Pay("BTCUSDT", Eleven, 0.001m);
        await rig.Minutes("BTCUSDT", Eleven, 5, close: 101);
        await rig.At(Eleven.AddMinutes(5).AddSeconds(15));
        Assert.That(
            rig.Ideas.Ideas.Where(idea => idea.StrategyName == "funding-short").Select(idea => (idea.Symbol, idea.Moment, idea.Side)),
            Is.EqualTo(new[] { ("BTCUSDT", Eleven.AddMinutes(5), EntrySide.Short) }));

        await rig.Minutes("BTCUSDT", Eleven.AddMinutes(5), 35, close: 101);
        await rig.At(Eleven.AddMinutes(40).AddSeconds(15));
        await rig.At(Eleven.AddMinutes(42));

        Idea idea = rig.Ideas.Ideas.Single(idea => idea.StrategyName == "funding-short");
        (DateTimeOffset moment, CycleOutcome cycle) = rig.Backtest("funding-short", FiveMinutes, "BTCUSDT", Eleven.AddMinutes(40)).Single();
        Assert.That(idea.State, Is.EqualTo(IdeaState.Closed));
        Assert.That((idea.Moment, idea.EntryTime, idea.ExitTime), Is.EqualTo((moment, (DateTimeOffset?)cycle.Fills[0].Moment, (DateTimeOffset?)cycle.Fills[^1].Moment)));
        Assert.That(idea.Result, Is.EqualTo(cycle.Net).Within(1e-9), "the result of the cycle of a backtest");
        Assert.That(rig.Log.Entries.Where(entry => entry.Level >= LogLevel.Warning), Is.Empty);
    }

    /// <summary>
    ///     Task 25.3 (б) under the rule of task 27.3: the Worker restarted between the payment of 11:00 and the close of 12:00.
    ///     The warm-up up to the close of 11:00 does not see the payment of 11:00 — it belongs to the next candle — so the live
    ///     close of 12:00 is the first time the rate is above the level, and the idea is born there, once. Before the fix the
    ///     warm-up put the payment on the candle of 11:00 and ate the event of the close of 12:00.
    /// </summary>
    [Test]
    public async Task A_restart_between_the_payment_and_the_next_close_gives_the_idea_once_at_the_next_close()
    {
        ScannerRig before = await FundingRig(OneHour);
        await before.Minutes("BTCUSDT", Ten, 60, close: 100);
        await before.At(Eleven.AddSeconds(15));
        await before.At(Eleven.AddSeconds(75));
        await before.Pay("BTCUSDT", Eleven, 0.001m);
        await before.At(Eleven.AddSeconds(76));
        Assert.That(before.Ideas.Ideas, Is.Empty, "the payment of 11:00 belongs to the candle of 12:00");

        var after = new ScannerRig();
        after.Time.SetUtcNow(Eleven.AddMinutes(32));
        after.History.Payments["btc"] = [.. Payments, new HistoryPayment(Eleven, "0.001")];
        await after.EnableFunding("funding-short", "30m", id: before.Strategies.Documents.Single().Id, timeframes: [OneHour]);
        await after.Start();
        await after.Pay("BTCUSDT", Ten.AddHours(-7), 0.0001m);
        await after.Pay("BTCUSDT", Ten.AddHours(-3), 0.0001m);
        await after.Pay("BTCUSDT", Eleven, 0.001m);
        await after.Minutes("BTCUSDT", Eleven, 60, close: 100);
        await after.At(Eleven.AddHours(1).AddSeconds(15));
        await after.At(Eleven.AddHours(1).AddSeconds(16));

        Assert.That(after.Ideas.Ideas.Select(idea => (idea.Symbol, idea.Moment)), Is.EqualTo(new[] { ("BTCUSDT", Eleven.AddHours(1)) }));
    }

    /// <summary>
    ///     The payments of ten pairs come some seconds apart, from 59 to 177 s after 11:00, as the data service fetches them.
    ///     The candle that closes after them, at 12:00, has all of them: its ideas are born in one pass, and «⚡ всплеск» counts
    ///     the pairs of the whole candle.
    /// </summary>
    [Test]
    public async Task The_ideas_of_the_candle_after_the_payments_are_born_together_with_one_burst()
    {
        Instrument[] pairs = Many(10);
        ScannerRig rig = await FundingRig(OneHour, pairs: pairs);
        foreach (Instrument pair in pairs)
        {
            await rig.Minutes(pair.Symbol, Ten, 60, close: 100);
        }

        await rig.At(Eleven.AddSeconds(15));
        for (int index = 0; index < pairs.Length; index++)
        {
            await rig.At(Eleven.AddSeconds(59 + (index * 13)));
            await rig.Pay(pairs[index].Symbol, Eleven, 0.001m);
        }

        await rig.At(Eleven.AddMinutes(3));
        Assert.That(rig.Ideas.Ideas, Is.Empty, "the payments of 11:00 belong to the candle of 12:00");
        foreach (Instrument pair in pairs)
        {
            await rig.Minutes(pair.Symbol, Eleven, 60, close: 100);
        }

        await rig.At(Eleven.AddHours(1).AddSeconds(15));
        Assert.That(rig.Ideas.Ideas, Has.Count.EqualTo(10));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Moment), Has.All.EqualTo(Eleven.AddHours(1)));
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
            await rig.Pay(pair.Symbol, Ten.AddHours(-7), 0.0001m);
            await rig.Pay(pair.Symbol, Ten.AddHours(-3), 0.0001m);
        }

        return rig;
    }
}
