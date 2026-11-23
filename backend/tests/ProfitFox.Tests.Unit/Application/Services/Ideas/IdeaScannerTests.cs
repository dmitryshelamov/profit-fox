using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, tasks 8.2–8.4, 8.6 and 8.7: the scanner of ideas on fakes — the warm-up, the pass on a
///     closed candle with the rule of one open idea, the reconciliation of the enabled strategies, the snapshot of the market
///     and the state of the scanner.
/// </summary>
public sealed class IdeaScannerTests
{
    private static readonly DateTimeOffset Eleven = Ten.AddHours(1);

    /// <summary>
    ///     Spec strategy-ideas «Все пары»: every warmed pair with a candle is computed at the close.
    /// </summary>
    [Test]
    public async Task An_enabled_strategy_runs_on_every_warmed_pair_that_has_the_candle()
    {
        var rig = new ScannerRig();
        Guid id = await rig.Enable("close-up", "8h");
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        Idea[] ideas = [.. rig.Ideas.Ideas.OrderBy(idea => idea.Symbol, StringComparer.Ordinal)];
        Assert.That(ideas.Select(idea => (idea.Symbol, idea.Moment, idea.StrategyId, idea.StrategyName, idea.StrategyVersion)), Is.EqualTo(new[]
        {
            ("BTCUSDT", Eleven, id, "close-up", 4), ("ETHUSDT", Eleven, id, "close-up", 4)
        }), "SOLUSDT has no minute in the candle");
        Assert.That(ideas.Select(idea => idea.CandleClose), Has.All.EqualTo(101m));
    }

    [Test]
    public async Task A_candle_is_not_computed_before_its_close_and_the_delay()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);

        await rig.At(Eleven.AddSeconds(10));

        Assert.That(rig.Ideas.Ideas, Is.Empty);
        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas, Has.Count.EqualTo(1));
    }

    /// <summary>
    ///     Task 18.15: when the REST is one candle behind at the warm-up, the candle between its last one and the boundary is
    ///     taken from the book, so <c>first-time</c> sees no hole: the close above 100 of 10:00 is not a first time at 11:00.
    /// </summary>
    [Test]
    public async Task The_candle_the_REST_has_not_yet_comes_from_the_book()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before - interval, "99");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        await rig.Minutes("ETHUSDT", Ten.AddHours(-1), 60, close: 101);
        await rig.Warm();

        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.EqualTo(new[] { "BTCUSDT" }), "ETHUSDT closed above 100 at 10:00 already");
    }

    /// <summary>
    ///     Task 18.13: a reconciliation without a change reads no graph and keeps the warmed runs; a new version is read once.
    /// </summary>
    [Test]
    public async Task A_reconciliation_without_a_change_reads_no_graph()
    {
        var rig = new ScannerRig();
        Guid id = await rig.Enable("close-up", "8h");
        await rig.Label("btc-down", "↓ BTC", TimeSpan.FromHours(1), BtcBelow(90));
        await rig.Start();
        (int strategies, int signals) = (rig.Strategies.Reads, rig.Signals.Reads);

        await rig.At(Ten.AddSeconds(15));
        await rig.At(Ten.AddSeconds(25));

        Assert.That((rig.Strategies.Reads, rig.Signals.Reads), Is.EqualTo((strategies, signals)), "the versions did not change");
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));
        await rig.Enable("close-up", "8h", version: 5, id: id);
        await rig.At(Ten.AddSeconds(35));
        Assert.That(rig.Strategies.Reads, Is.EqualTo(strategies + 1), "the new version is read once");
    }

    /// <summary>
    ///     Task 18.10, confirmed: with the minutes of two pairs mixed, the time of an idea and a payment of its pair wait for
    ///     the minutes of its pair to reach their moment, so a stop in the last minute and the price of the payment are those
    ///     of a backtest.
    /// </summary>
    [Test]
    public async Task The_time_and_the_payments_of_an_idea_wait_for_the_minutes_of_its_pair()
    {
        var rig = new ScannerRig();
        Idea idea = ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas.IdeaGenerator.Generate(
            side: ProfitFox.Domain.ValueObjects.Graph.Enums.EntrySide.Long, moment: Ten, after: TimeSpan.FromHours(1), stop: 0.02, close: 100m, timeframe: TimeSpan.FromHours(1));
        rig.Ideas.Ideas.Add(idea);
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 29, close: 100);
        await rig.Minutes("ETHUSDT", Ten, 32, close: 100);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.FundingPayment, "BTCUSDT", Ten.AddMinutes(30), 0.001m), CancellationToken.None);
        await rig.At(Ten.AddMinutes(32).AddSeconds(20));
        Assert.That(idea.FundingPaid, Is.Zero, "the minute 10:29 of BTCUSDT has not come yet");

        await rig.Minutes("BTCUSDT", Ten.AddMinutes(29), 30, close: 105);
        await rig.Minutes("ETHUSDT", Ten.AddMinutes(32), 29, close: 100);
        await rig.At(Eleven.AddMinutes(1).AddSeconds(20));

        Assert.That(idea.FundingPaid, Is.EqualTo(0.001 * 105).Within(1e-12), "priced at the close of BTCUSDT before 10:30, not the one of 10:28");
        Assert.That(idea.ExitTime, Is.Null, "the minute 10:59 of BTCUSDT has not come yet");

        await rig.Scanner.ApplyAsync(new LiveMinute("BTCUSDT", Ten.AddMinutes(59), 105, 105, 90, 95, 1, 95), CancellationToken.None);
        await rig.At(Eleven.AddMinutes(3));

        Assert.That((idea.State, idea.ExitReason, idea.ExitTime), Is.EqualTo((IdeaState.Closed, ExitReason.Stop, (DateTimeOffset?)Ten.AddMinutes(59))));
    }

    /// <summary>
    ///     Task 18.9: a usual start asks the feed for the whole day it holds, counted when the feed subscribes, so no hole is
    ///     told; an open idea counted only up to before that day asks from its minute, and the hole is told.
    /// </summary>
    [Test]
    public async Task A_usual_start_asks_for_the_whole_day_and_counts_ideas_since_the_start()
    {
        var rig = new ScannerRig();
        await rig.Start();
        Assert.That(rig.Scanner.CatchUpFrom, Is.Null, "the whole day the feed holds");

        await rig.Minutes("BTCUSDT", Ten, 1, close: 100);
        await rig.At(Ten.AddMinutes(1).AddSeconds(5));
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)), "since the start");

        var restarted = new ScannerRig();
        Idea old = ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas.IdeaGenerator.Generate(moment: Ten.AddDays(-2), after: TimeSpan.FromDays(3));
        old.ComputedUntil = Ten.AddDays(-2).AddHours(1);
        Idea recent = ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas.IdeaGenerator.Generate(moment: Ten.AddHours(-3));
        restarted.Ideas.Ideas.Add(recent);
        await restarted.Scanner.StartAsync(CancellationToken.None);
        Assert.That(restarted.Scanner.CatchUpFrom, Is.Null, "an idea within the day needs no more");
        restarted.Ideas.Ideas.Add(old);
        await restarted.Scanner.StartAsync(CancellationToken.None);
        Assert.That(restarted.Scanner.CatchUpFrom, Is.EqualTo(Ten.AddDays(-2).AddHours(1)), "from the minute the old idea is counted to");
    }

    /// <summary>
    ///     Task 18.8 (а): a pair that left the catalog and came back is warmed up again from nothing and gives ideas.
    /// </summary>
    [Test]
    public async Task A_pair_that_left_the_catalog_and_came_back_is_warmed_up_again()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        await rig.Minutes("SOLUSDT", Ten, 10, close: 99);

        await rig.Scanner.ApplyAsync(new MarketPairs([.. Pairs.Where(pair => pair.Symbol != "SOLUSDT")]), CancellationToken.None);
        await rig.At(Ten.AddSeconds(30));
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        rig.Time.SetUtcNow(Ten.AddSeconds(40));
        await rig.Warm();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));
        await rig.Minutes("SOLUSDT", Ten.AddMinutes(10), 50, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.EqualTo(new[] { "SOLUSDT" }));
    }

    /// <summary>
    ///     Task 18.8 (а): a pair whose graph fails goes back to waiting with a line in the journal; the other pairs go on.
    /// </summary>
    [Test]
    public async Task A_pair_whose_graph_fails_waits_and_the_others_go_on()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => [.. Warmup(interval, before, "99"), .. Warmup(interval, before, "99")];
        await rig.Enable("close-up", "8h");
        await rig.Start();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)), "ETHUSDT got its candles twice");
        Assert.That(rig.Log.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("ETHUSDT")), Is.True);
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.EqualTo(new[] { "BTCUSDT" }));
    }

    /// <summary>
    ///     Task 18.8 (б): the ideas of a candle are written once the database answers again, not lost with the failed write.
    /// </summary>
    [Test]
    public async Task The_ideas_of_a_candle_are_written_after_a_failed_write()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        rig.Ideas.FailingAdds = 1;

        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas, Has.Count.LessThan(2));
        await rig.At(Eleven.AddSeconds(16));

        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.Symbol, idea.Moment)).Order(), Is.EqualTo(new[] { ("BTCUSDT", Eleven), ("ETHUSDT", Eleven) }));
        Assert.That(rig.Scanner.Open, Has.Count.EqualTo(2));
    }

    /// <summary>
    ///     Task 18.8 (в): a failed write of the snapshot of the market does not stop the ideas.
    /// </summary>
    [Test]
    public async Task A_failed_snapshot_does_not_stop_the_ideas()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        rig.Market.Failing = true;
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);

        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.EqualTo(new[] { "BTCUSDT" }));
        Assert.That(rig.Log.Entries.Any(entry => entry.Level == LogLevel.Error), Is.True, "the failed snapshot is in the journal");
        rig.Market.Failing = false;
        await rig.Minutes("BTCUSDT", Eleven, 1, close: 101);
        await rig.At(Eleven.AddMinutes(1).AddSeconds(1));
        Assert.That(rig.Market.Pairs, Is.Not.Empty, "written again on the next minute");
    }

    /// <summary>
    ///     Task 18.6: the open ideas a minute moved are written by one statement a pass, however many there are.
    /// </summary>
    [Test]
    public async Task Ten_thousand_open_ideas_are_written_by_one_statement_a_minute()
    {
        var rig = new ScannerRig();
        rig.Ideas.Ideas.AddRange(Enumerable.Range(0, 10_000).Select(_ => ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas.IdeaGenerator.Generate(moment: Ten, timeframe: TimeSpan.FromHours(1))));
        await rig.Start();
        rig.Ideas.Writes.Clear();

        await rig.Minutes("BTCUSDT", Ten, 1, close: 101);
        await rig.At(Ten.AddMinutes(1).AddSeconds(15));

        Assert.That(rig.Ideas.Writes, Is.EqualTo(new[] { 10_000 }), "one statement for the 10 000 ideas of the minute");
        Assert.That(rig.Scanner.Open.Select(idea => idea.Now).Distinct().Count(), Is.EqualTo(1));
        Assert.That(rig.Scanner.Open[0].Now, Is.Not.Null);
    }

    /// <summary>
    ///     Task 18.3: a refused warm-up is written to the journal no more than once a minute for a reason, the state counts
    ///     the pairs it refused and tells the last reason, and a pair that warms up later leaves the count.
    /// </summary>
    [Test]
    public async Task A_refused_warmup_is_written_to_the_journal_and_counted()
    {
        var rig = new ScannerRig();
        const string refusal = "The REST of the market-data service answered 400 to candles: from is required";
        rig.History.Failing["eth"] = refusal;
        rig.History.Failing["sol"] = refusal;
        await rig.Enable("close-up", "8h");
        await rig.Start();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((1, 3)));
        Assert.That(rig.Scanner.Failures, Is.EqualTo((2, refusal)));
        Assert.That(rig.Log.Entries.Where(entry => entry.Message.Contains(refusal)).Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }), "one line for the reason");

        await rig.At(Ten.AddSeconds(50));
        Assert.That(rig.Log.Entries.Count(entry => entry.Message.Contains(refusal)), Is.EqualTo(1), "not again within the minute");

        rig.History.Failing.Remove("eth");
        rig.Time.SetUtcNow(Ten.AddMinutes(1).AddSeconds(10));
        await rig.Warm();
        Assert.That(rig.Scanner.Failures, Is.EqualTo((1, refusal)), "ETHUSDT warmed up");
        Assert.That(rig.Log.Entries.Count(entry => entry.Message.Contains(refusal)), Is.EqualTo(2), "a minute later the reason is written again");

        rig.History.Failing.Clear();
        rig.Time.SetUtcNow(Ten.AddMinutes(2).AddSeconds(20));
        await rig.Warm();
        Assert.That(rig.Scanner.Failures, Is.EqualTo((0, (string?)null)));
    }

    /// <summary>
    ///     Spec strategy-ideas «Пара не прогрета»: a pair whose history is loading gives no ideas, and is asked again later.
    /// </summary>
    [Test]
    public async Task A_pair_whose_history_is_loading_gives_no_ideas_until_it_is_warmed()
    {
        var rig = new ScannerRig();
        rig.History.Loading.Add("sol");
        await rig.Enable("close-up", "8h");
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.Minutes("SOLUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.EqualTo(new[] { "BTCUSDT" }));
        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)));
        int asked = rig.History.Asked.Count(item => item == "candles sol");
        await rig.At(Eleven.AddSeconds(45));
        Assert.That(rig.History.Asked.Count(item => item == "candles sol"), Is.EqualTo(asked), "not again within the minute");

        rig.History.Loading.Clear();
        rig.Time.SetUtcNow(Eleven.AddMinutes(1).AddSeconds(15));
        await rig.Warm();
        await rig.Minutes("SOLUSDT", Eleven, 60, close: 99);
        await rig.Minutes("SOLUSDT", Eleven.AddHours(1), 60, close: 101);
        await rig.At(Eleven.AddHours(2).AddSeconds(15));

        Assert.That(rig.History.Asked.Count(item => item == "candles sol"), Is.EqualTo(asked + 1), "asked again after a minute");
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));
        Assert.That(rig.Ideas.Ideas.Where(idea => idea.Symbol == "SOLUSDT").Select(idea => idea.Moment), Is.EqualTo(new[] { Eleven.AddHours(2) }));
    }

    /// <summary>
    ///     Spec strategy-ideas «Свеча прогрева»: an order of the graph on a candle of the warm-up is no idea.
    /// </summary>
    [Test]
    public async Task An_order_on_a_candle_of_the_warm_up_is_no_idea()
    {
        var rig = new ScannerRig();
        rig.History.Candles["btc"] = (interval, before) =>
        [
            .. Warmup(interval, before, "99").Select(candle => candle.OpenTime == Ten.AddDays(-2) ? candle : candle with { Close = candle.OpenTime == Ten.AddDays(-2).AddHours(1) ? "101" : "99" })
        ];
        await rig.Enable("close-up", "8h");

        await rig.Start();
        await rig.At(Ten.AddMinutes(2));

        Assert.That(rig.Ideas.Ideas, Is.Empty);
    }

    /// <summary>
    ///     Spec strategy-ideas «Одна открытая идея»: «Вторая заявка в сроке первой», «Другой таймфрейм», «После закрытия».
    /// </summary>
    [Test]
    public async Task A_second_order_in_the_time_of_an_open_idea_is_skipped_on_its_timeframe_only()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h", timeframes: [TimeSpan.FromHours(1), TimeSpan.FromHours(4)]);
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 120, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        await rig.At(Eleven.AddHours(1).AddSeconds(15));
        await rig.Minutes("BTCUSDT", Eleven.AddHours(1), 60, close: 99);
        await rig.At(Eleven.AddHours(2).AddSeconds(15));
        await rig.Minutes("BTCUSDT", Eleven.AddHours(2), 60, close: 101);
        await rig.At(Eleven.AddHours(3).AddSeconds(15));

        Assert.That(
            rig.Ideas.Ideas.Select(idea => (idea.Timeframe, idea.Moment)),
            Is.EquivalentTo(new[] { (TimeSpan.FromHours(1), Eleven), (TimeSpan.FromHours(4), Eleven.AddHours(1)) }),
            "the 1h order of 14:00 comes in the time of the idea of 11:00; the 4h candle of 12:00 is another timeframe");
    }

    [Test]
    public async Task An_order_at_the_close_where_the_former_idea_ends_is_a_new_idea()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "2h");
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        await rig.Minutes("BTCUSDT", Eleven, 60, close: 99);
        await rig.At(Eleven.AddHours(1).AddSeconds(15));
        await rig.Minutes("BTCUSDT", Eleven.AddHours(1), 60, close: 101);
        await rig.At(Eleven.AddHours(2).AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Moment), Is.EqualTo(new[] { Eleven, Eleven.AddHours(2) }), "the idea of 11:00 ends at 13:00 by its time");
    }

    /// <summary>
    ///     Spec strategy-ideas «Новая версия»: the ideas after its warm-up carry the new version, the open idea of the former
    ///     one goes on by its plan.
    /// </summary>
    [Test]
    public async Task A_new_version_gives_the_ideas_after_its_warm_up_and_the_open_one_of_the_former_goes_on()
    {
        var rig = new ScannerRig();
        Guid id = await rig.Enable("close-up", "8h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        await rig.Enable("close-up", "1h", version: 5, id: id);
        rig.Time.SetUtcNow(Eleven.AddSeconds(30));
        await rig.Warm();
        await rig.Minutes("ETHUSDT", Eleven, 60, close: 101);
        await rig.Minutes("BTCUSDT", Eleven, 60, close: 102);
        await rig.At(Eleven.AddHours(1).AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.Symbol, idea.StrategyVersion, idea.After)), Is.EquivalentTo(new[]
        {
            ("BTCUSDT", 4, TimeSpan.FromHours(8)), ("ETHUSDT", 5, TimeSpan.FromHours(1))
        }));
        Assert.That(rig.Scanner.Open.Single(idea => idea.Symbol == "BTCUSDT").State, Is.EqualTo(IdeaState.Open));
    }

    /// <summary>
    ///     Spec strategy-ideas «Выключение» and «Удаление стратегии»: no new ideas, and the open ones close by their plan.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_strategy_turned_off_or_deleted_gives_no_new_ideas_and_its_open_ones_close(bool deleted)
    {
        var rig = new ScannerRig();
        Guid id = await rig.Enable("close-up", "1h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        if (deleted)
        {
            await rig.Strategies.DeleteAsync("close-up", CancellationToken.None);
            rig.Enabled.Remove(id);
        }
        else
        {
            await rig.Enabled.SetAsync(new StrategyIdeas(id, false, [TimeSpan.FromHours(1)]), CancellationToken.None);
        }

        await rig.At(Eleven.AddSeconds(30));
        await rig.Minutes("BTCUSDT", Eleven, 60, close: 99);
        await rig.Minutes("ETHUSDT", Eleven, 60, close: 101);
        await rig.At(Eleven.AddHours(1).AddSeconds(15));
        await rig.At(Eleven.AddHours(1).AddMinutes(2));

        Idea idea = rig.Ideas.Ideas.Single();
        Assert.That((idea.Symbol, idea.State, idea.ExitReason, idea.ExitTime), Is.EqualTo(("BTCUSDT", IdeaState.Closed, ExitReason.Time, (DateTimeOffset?)Eleven.AddHours(1))));
        Assert.That(idea.Result, Is.Not.Null);
    }

    /// <summary>
    ///     Spec market-overview «Снимок рынка»: the snapshot of every pair is written after a closed minute, with the time of
    ///     the next payment.
    /// </summary>
    [Test]
    public async Task The_snapshot_of_the_market_is_written_after_each_closed_minute()
    {
        var rig = new ScannerRig();
        await rig.Start();
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.FundingPayment, "SOLUSDT", Ten.AddHours(-16), 0.0001m), CancellationToken.None);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.FundingPayment, "SOLUSDT", Ten.AddHours(-8), 0.0002m), CancellationToken.None);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.OpenInterest, "SOLUSDT", Ten, 24_000), CancellationToken.None);
        await rig.Minutes("SOLUSDT", Ten, 1, close: 238.42m);

        await rig.At(Ten.AddMinutes(1).AddSeconds(2));

        MarketPair sol = rig.Market.Pairs.Single();
        Assert.That((sol.Symbol, sol.Price, sol.OpenInterestUsdt, sol.NextFunding), Is.EqualTo(("SOLUSDT", 238.42m, (decimal?)5_722_080m, (DateTimeOffset?)Ten)));
        Assert.That(rig.Market.Writes, Is.EqualTo(1));

        await rig.At(Ten.AddMinutes(1).AddSeconds(30));
        Assert.That(rig.Market.Writes, Is.EqualTo(1), "no new minute, no new snapshot");
        await rig.Minutes("BTCUSDT", Ten.AddMinutes(1), 1, close: 96000);
        await rig.At(Ten.AddMinutes(2).AddSeconds(2));
        Assert.That(rig.Market.Pairs.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "SOLUSDT" }));
    }

    /// <summary>
    ///     Spec strategy-ideas «Состояние сканера в разделе „Идеи“»: how many pairs are warmed, and since when ideas count
    ///     without a break.
    /// </summary>
    [Test]
    public async Task The_state_of_the_scanner_tells_the_warm_up_and_since_when_it_counts_without_a_break()
    {
        var rig = new ScannerRig();
        rig.History.Loading.Add("eth");
        await rig.Enable("close-up", "1h");
        await rig.Start();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)));
        Assert.That(rig.Scanner.LiveSince, Is.Null, "the feed has not caught up");

        await rig.Minutes("BTCUSDT", Ten, 1, close: 100);
        await rig.At(Ten.AddMinutes(1).AddSeconds(5));
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)), "since the start");

        await rig.Scanner.ApplyAsync(new FeedGap("minutes", Ten.AddMinutes(1), Ten.AddMinutes(30)), CancellationToken.None);
        Assert.That(rig.Scanner.LiveSince, Is.Null);
        await rig.Minutes("BTCUSDT", Ten.AddMinutes(30), 1, close: 100);
        await rig.At(Ten.AddMinutes(31).AddSeconds(5));
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddMinutes(31).AddSeconds(5)), "since the end of the hole");
    }

    /// <summary>
    ///     Spec strategy-ideas «Исключённая идея»: a hole in the minutes of an open idea or a pair that left the set exclude it.
    /// </summary>
    [Test]
    public async Task A_hole_in_the_live_data_and_a_delisted_pair_exclude_the_open_ideas()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "4h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        await rig.Scanner.ApplyAsync(new FeedGap("minutes", Eleven.AddMinutes(5), Eleven.AddHours(48)), CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs([Pairs[1], Pairs[2]]), CancellationToken.None);
        await rig.At(Eleven.AddMinutes(1));

        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.Symbol, idea.State, idea.Excluded)), Is.EquivalentTo(new[]
        {
            ("BTCUSDT", IdeaState.Excluded, (string?)"нет данных"), ("ETHUSDT", IdeaState.Excluded, "нет данных")
        }));
    }
}
