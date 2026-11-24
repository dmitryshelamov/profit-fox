using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
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
    ///     Task 19.3: while the book still catches up the day, the candle at the join of the warm-up is not taken from the part
    ///     of its minutes the book has: the pair waits, and is warm with the whole candle once the book closed it. The minutes
    ///     of 9:00–9:29 close at 99, those of 9:30–9:59 at 101: the candle of 9:00 closes at 101, so 11:00 is no first time.
    /// </summary>
    [Test]
    public async Task The_candle_at_the_join_waits_for_the_book_to_close_it()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before - interval, "99");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        await rig.Minutes("ETHUSDT", Ten.AddHours(-1), 30, close: 99);
        rig.Time.SetUtcNow(Ten.AddSeconds(20));
        await rig.Warm();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)), "the book caught up half of the hour of 9:00");
        int asked = rig.History.Asked.Count(item => item == "candles eth");

        await rig.Minutes("ETHUSDT", Ten.AddHours(-1).AddMinutes(30), 30, close: 101);
        await rig.At(Ten.AddSeconds(25));
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)), "the book closed the candle of 9:00");
        Assert.That(rig.History.Asked.Count(item => item == "candles eth"), Is.EqualTo(asked), "the history read is not asked again");

        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.EqualTo(new[] { "BTCUSDT" }), "ETHUSDT closed above 100 at 10:00 already");
    }

    /// <summary>
    ///     Task 20.5, confirmed: a catch-up told one pair at a time — the minutes of BTCUSDT up to 10:09 first, those of
    ///     ETHUSDT after them — closes the boundary of 10:00 for the book before the minutes of ETHUSDT come. Joined on the book
    ///     alone, ETHUSDT was warm without the candle of 9:00 at the join, and its close above 100 at 10:00 was a first time at
    ///     11:00. Before the feed is live the pair waits for its own minute of the boundary.
    /// </summary>
    [Test]
    public async Task A_catch_up_one_pair_at_a_time_joins_the_warmup_with_the_minutes_of_its_own_pair()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before - interval, "99");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        await rig.Minutes("BTCUSDT", Ten.AddHours(-1), 70, close: 99);
        rig.Time.SetUtcNow(Ten.AddMinutes(10).AddSeconds(20));
        await rig.Warm();
        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)), "the minutes of ETHUSDT at the join are still to come");

        await rig.Minutes("ETHUSDT", Ten.AddHours(-1), 70, close: 101);
        await rig.At(Ten.AddMinutes(10).AddSeconds(30), quiet: false);
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));

        await rig.Minutes("BTCUSDT", Ten.AddMinutes(10), 50, close: 99);
        await rig.Minutes("ETHUSDT", Ten.AddMinutes(10), 50, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Symbol), Is.Empty, "ETHUSDT closed above 100 at 9:00 already");
    }

    /// <summary>
    ///     Task 20.5: a quiet pair — no minute since long before the boundary — does not wait for ever: once the feed is live,
    ///     the pair without minutes is joined.
    /// </summary>
    [Test]
    public async Task A_quiet_pair_is_joined_once_the_feed_is_live()
    {
        var rig = new ScannerRig();
        rig.History.Candles["sol"] = (interval, before) => Warmup(interval, before - interval, "99");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        await rig.Minutes("BTCUSDT", Ten.AddHours(-1), 70, close: 99);
        await rig.Minutes("SOLUSDT", Ten.AddHours(-1), 10, close: 99);
        rig.Time.SetUtcNow(Ten.AddMinutes(10).AddSeconds(20));
        await rig.Warm();
        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)), "SOLUSDT may still have minutes to come");

        await rig.At(Ten.AddMinutes(10).AddSeconds(40));
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)), "the feed is live and SOLUSDT had no trades since 9:09");
        Assert.That(rig.Scanner.Failures.Failed, Is.Zero);
    }

    /// <summary>
    ///     Task 20.4: a hole told after the read leaves the book without the minutes of the candle at the join; once the book
    ///     closed the boundary the pair does not wait for it: the warm-up failed with its reason, and the pair is read from the
    ///     REST again after the pause.
    /// </summary>
    [Test]
    public async Task A_hole_after_the_read_fails_the_warmup_and_the_pair_is_read_again()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before - interval, "99");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        await rig.Minutes("ETHUSDT", Ten.AddHours(-1), 30, close: 99);
        rig.Time.SetUtcNow(Ten.AddSeconds(20));
        await rig.Warm();
        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)), "ETHUSDT waits for the book to close the candle of 9:00");

        await rig.Scanner.ApplyAsync(new FeedGap("minutes", Ten.AddHours(-1).AddMinutes(30), Ten.AddMinutes(5)), CancellationToken.None);
        await rig.Minutes("ETHUSDT", Ten.AddMinutes(5), 2, close: 99);
        await rig.At(Ten.AddMinutes(7).AddSeconds(20));

        (int failed, string? reason) = rig.Scanner.Failures;
        Assert.That(failed, Is.EqualTo(1), "the candle at the join will not come: no endless wait");
        Assert.That(reason, Does.Contain("09:00"));
        Assert.That(rig.Log.Entries.Count(entry => entry.Message.Contains("ETHUSDT")), Is.EqualTo(1));
        int asked = rig.History.Asked.Count(item => item == "candles eth");

        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before, "99");
        rig.Time.SetUtcNow(Ten.AddMinutes(8).AddSeconds(30));
        await rig.Warm();
        Assert.That(rig.History.Asked.Count(item => item == "candles eth"), Is.EqualTo(asked + 1), "read from the REST again after the pause");
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));
        Assert.That(rig.Scanner.Failures, Is.EqualTo((0, (string?)null)));
    }

    /// <summary>
    ///     Task 20.4: the REST lags further back than the first minute of the book — the candle at the join opened before the
    ///     book begins, so the book closing the boundary never holds it: the warm-up fails with its reason and is read again.
    /// </summary>
    [Test]
    public async Task A_REST_behind_the_start_of_the_book_fails_the_warmup_and_the_pair_is_read_again()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before - (3 * interval), "99");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);
        await rig.Minutes("ETHUSDT", Ten.AddHours(-1), 60, close: 99);
        rig.Time.SetUtcNow(Ten.AddSeconds(20));
        await rig.Warm();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)), "the REST ends at 7:00, the book begins at 9:00");
        Assert.That(rig.Scanner.Failures.Failed, Is.EqualTo(1));
        int asked = rig.History.Asked.Count(item => item == "candles eth");

        rig.History.Candles["eth"] = (interval, before) => Warmup(interval, before, "99");
        rig.Time.SetUtcNow(Ten.AddMinutes(1).AddSeconds(30));
        await rig.Warm();
        Assert.That(rig.History.Asked.Count(item => item == "candles eth"), Is.EqualTo(asked + 1));
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));
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
    ///     Task 19.9, confirmed: a catch-up told in batches by pair — the minutes of BTCUSDT up to 10:40 first, those of ETHUSDT
    ///     after them — does not put the time of an idea of ETHUSDT before its minutes: while the feed catches up, the time and
    ///     the payments wait for the minutes of the pair, and the stop in its minute 10:29 is the exit, as in a backtest. The
    ///     rule of two minutes is left to the live feed: a pair without a trade then gets its time once the feed went quiet.
    /// </summary>
    [Test]
    public async Task A_catch_up_in_batches_by_pair_puts_neither_the_time_nor_a_payment_before_the_minutes_of_the_pair()
    {
        var rig = new ScannerRig();
        Idea idea = ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas.IdeaGenerator.Generate(
            symbol: "ETHUSDT", side: ProfitFox.Domain.ValueObjects.Graph.Enums.EntrySide.Long, moment: Ten, after: TimeSpan.FromMinutes(30), stop: 0.02, close: 100m, timeframe: TimeSpan.FromHours(1));
        Idea quiet = ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas.IdeaGenerator.Generate(
            symbol: "SOLUSDT", side: ProfitFox.Domain.ValueObjects.Graph.Enums.EntrySide.Long, moment: Ten, after: TimeSpan.FromMinutes(30), close: 100m, timeframe: TimeSpan.FromHours(1));
        rig.Ideas.Ideas.Add(idea);
        rig.Ideas.Ideas.Add(quiet);
        await rig.Start();
        await rig.Minutes("SOLUSDT", Ten, 1, close: 100);

        await rig.Minutes("BTCUSDT", Ten, 41, close: 100);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.FundingPayment, "ETHUSDT", Ten.AddMinutes(20), 0.001m), CancellationToken.None);
        await rig.At(Ten.AddMinutes(40).AddSeconds(20), quiet: false);
        Assert.That((idea.State, idea.ExitTime, idea.FundingPaid), Is.EqualTo((IdeaState.Open, (DateTimeOffset?)null, 0d)), "the minutes of ETHUSDT are still to come: not «нет сделок»");

        await rig.Minutes("ETHUSDT", Ten, 29, close: 101);
        await rig.Scanner.ApplyAsync(new LiveMinute("ETHUSDT", Ten.AddMinutes(29), 101, 101, 97, 98, 1, 98), CancellationToken.None);
        await rig.At(Ten.AddMinutes(40).AddSeconds(30), quiet: false);
        Assert.That(quiet.ExitTime, Is.Null, "SOLUSDT had no trade, and the feed still catches up");

        await rig.At(Ten.AddMinutes(42).AddSeconds(30));
        Assert.That((idea.State, idea.ExitReason, idea.ExitTime), Is.EqualTo((IdeaState.Closed, ExitReason.Stop, (DateTimeOffset?)Ten.AddMinutes(29))));
        Assert.That(idea.FundingPaid, Is.EqualTo(0.001 * 101).Within(1e-12), "priced at the close of ETHUSDT before 10:20");
        Assert.That((quiet.ExitReason, quiet.ExitTime), Is.EqualTo(((ExitReason?)ExitReason.Time, (DateTimeOffset?)Ten.AddMinutes(30))), "the live feed went quiet: the rule of two minutes");
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
    ///     Task 19.4: the refusals of 831 pairs whose bodies differ (a trace id, the moments of the window) are one reason — the
    ///     code and what was asked — and give one line a minute; the line still has a body.
    /// </summary>
    [Test]
    public async Task Refusals_with_different_bodies_give_one_line_a_minute()
    {
        Instrument[] pairs = Many(831);
        var rig = new ScannerRig(pairs);
        for (int index = 0; index < pairs.Length; index++)
        {
            rig.History.Refusing[pairs[index].Id] = new HistoryUnavailableException(
                $$"""The REST of the market-data service answered 400 to candles: { "traceId": "{{index}}" }""",
                reason: "The REST of the market-data service answered 400 to candles");
        }

        await rig.Enable("close-up", "8h");
        await rig.Start();

        string[] lines = [.. rig.Log.Entries.Select(entry => entry.Message).Where(message => message.Contains("answered 400 to candles"))];
        Assert.That(lines, Has.Length.EqualTo(1));
        Assert.That(lines[0], Does.Contain("traceId"));
        Assert.That(rig.Scanner.Failures.Failed, Is.EqualTo(831));

        rig.Time.SetUtcNow(Ten.AddMinutes(1).AddSeconds(10));
        await rig.Warm();
        Assert.That(rig.Log.Entries.Count(entry => entry.Message.Contains("answered 400 to candles")), Is.EqualTo(2), "a minute later once more");
    }

    /// <summary>
    ///     Task 19.2: the timeout of the REST cancels the read while the scanner is not stopped — a failed warm-up with its
    ///     reason, not a stop: the pair is counted as failed, written to the journal and warmed up on the next try.
    /// </summary>
    [Test]
    public async Task A_warmup_read_that_timed_out_fails_and_is_warmed_up_on_the_next_try()
    {
        var rig = new ScannerRig();
        rig.History.TimingOut.Add("sol");
        await rig.Enable("close-up", "8h");
        await rig.Start();

        Assert.That(rig.Scanner.Progress, Is.EqualTo((2, 3)));
        (int failed, string? reason) = rig.Scanner.Failures;
        Assert.That(failed, Is.EqualTo(1));
        Assert.That(reason, Does.Contain("HttpClient.Timeout"));
        Assert.That(rig.Log.Entries.Where(entry => entry.Message.Contains("SOLUSDT")).Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));

        rig.History.TimingOut.Clear();
        rig.Time.SetUtcNow(Ten.AddMinutes(1).AddSeconds(10));
        await rig.Warm();
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)));
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
    ///     Task 24.1, confirmed: with no new minute the candles do not close and no idea is born — but the state went on saying
    ///     that ideas count without a break since the start. A feed silent for longer than the limit clears it; once the feed
    ///     comes back, the candles that closed in the silence are computed from its catch-up, and ideas count without a break
    ///     again since the feed came back.
    /// </summary>
    [Test]
    public async Task A_feed_silent_for_longer_than_the_limit_is_no_live_state_and_its_candles_are_computed_once_it_comes_back()
    {
        var rig = new ScannerRig();
        Guid id = await rig.Enable("close-up", "8h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 3, close: 99);
        await rig.At(Ten.AddMinutes(3).AddSeconds(5));
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)), "since the start");

        await rig.At(Ten.AddMinutes(6));
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)), "three minutes after the last minute is within the limit");
        await rig.At(Ten.AddMinutes(6).AddSeconds(1));
        Assert.That(rig.Scanner.LiveSince, Is.Null, "the feed has told no minute for longer than the limit");
        Assert.That(rig.Log.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1));

        await rig.At(Eleven.AddSeconds(15));
        Assert.That(rig.Ideas.Ideas, Is.Empty, "without minutes the candle of 10:00 does not close");
        Assert.That(rig.Scanner.LiveSince, Is.Null);

        await rig.Minutes("BTCUSDT", Ten.AddMinutes(3), 57, close: 101);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.Minutes("BTCUSDT", Eleven, 1, close: 101);
        await rig.At(Eleven.AddMinutes(1).AddSeconds(5), quiet: false);
        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.Symbol, idea.Moment, idea.StrategyId)).OrderBy(idea => idea.Symbol, StringComparer.Ordinal), Is.EqualTo(new[]
        {
            ("BTCUSDT", Eleven, id), ("ETHUSDT", Eleven, id)
        }), "the candle that closed in the silence is computed from the catch-up");
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Eleven.AddMinutes(1).AddSeconds(5)), "without a break since the feed came back");
        Assert.That(rig.Log.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1), "one warning for the silence");
    }

    /// <summary>
    ///     Task 24.1: a quiet night — the minutes of one pair come, the others have no trades — keeps the live state.
    /// </summary>
    [Test]
    public async Task A_quiet_night_keeps_the_live_state()
    {
        var rig = new ScannerRig();
        await rig.Start();
        for (int minute = 0; minute < 20; minute++)
        {
            await rig.Minutes("BTCUSDT", Ten.AddMinutes(minute), 1, close: 100);
            await rig.At(Ten.AddMinutes(minute + 1).AddSeconds(5));
        }

        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)));
        Assert.That(rig.Log.Entries.Where(entry => entry.Level == LogLevel.Warning), Is.Empty);
    }

    /// <summary>
    ///     Task 25.2: <c>Scanner:FeedSilence</c> of zero switches the watchdog off for the scanner too — ten minutes without a
    ///     minute, a pass every second, keep the live state and write nothing to the journal.
    /// </summary>
    [Test]
    public async Task A_feed_silence_of_zero_does_not_watch_the_silence()
    {
        var rig = new ScannerRig(settings: new ScannerSettings { FeedSilence = TimeSpan.Zero });
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 1, close: 100);
        await rig.At(Ten.AddMinutes(1).AddSeconds(5));
        Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)), "since the start");

        for (int second = 1; second <= 600; second++)
        {
            await rig.At(Ten.AddMinutes(1).AddSeconds(5 + second));
            Assert.That(rig.Scanner.LiveSince, Is.EqualTo(Ten.AddSeconds(5)), $"{second} s without a minute");
        }

        Assert.That(rig.Log.Entries, Is.Empty);
    }

    /// <summary>
    ///     Task 25.1: a catch-up told by minute and within a minute by pair, long after the close of 11:00 — a pass that falls
    ///     between the minute 10:59 of BTCUSDT and that of ETHUSDT does not compute the candle of 10:00 of ETHUSDT without its
    ///     last minute (the minutes close at 99, the last one at 101): until the feed is live, the boundary waits for a minute
    ///     that opens at it, and the ideas are those of the whole batch.
    /// </summary>
    [Test]
    public async Task A_pass_in_the_middle_of_the_minute_before_a_boundary_of_a_catch_up_waits_for_the_whole_batch()
    {
        Idea[] whole = await CatchUpAcrossTheBoundary(passInTheMiddle: false);
        Assert.That(whole.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose)), Is.EqualTo(new[]
        {
            ("BTCUSDT", Eleven, 101m), ("ETHUSDT", Eleven, 101m)
        }));

        Idea[] split = await CatchUpAcrossTheBoundary(passInTheMiddle: true);
        Assert.That(
            split.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose, string.Join(",", idea.Labels))).ToArray(),
            Is.EqualTo(whole.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose, string.Join(",", idea.Labels))).ToArray()),
            "the same ideas as from the whole batch");
    }

    /// <summary>
    ///     Task 25.1: a catch-up that ends exactly at the boundary — the minute 10:59 is the newest the service has — is not lost:
    ///     the candle of 10:00 waits while the feed catches up and is computed once the feed is live.
    /// </summary>
    [Test]
    public async Task The_boundary_at_the_end_of_a_catch_up_is_computed_once_the_feed_is_live()
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        rig.Time.SetUtcNow(Eleven.AddSeconds(30));

        await CatchUp(rig, Ten, 59, close: 99);
        await rig.Scanner.ApplyAsync(Minute("BTCUSDT", Ten.AddMinutes(59), close: 101), CancellationToken.None);
        await rig.At(Eleven.AddSeconds(31), quiet: false);
        Assert.That(rig.Ideas.Ideas, Is.Empty, "the minute 10:59 of ETHUSDT is still to come");

        await rig.Scanner.ApplyAsync(Minute("ETHUSDT", Ten.AddMinutes(59), close: 101), CancellationToken.None);
        await rig.At(Eleven.AddSeconds(32), quiet: false);
        Assert.That(rig.Ideas.Ideas, Is.Empty, "the feed still catches up: the batch may not be whole");

        await rig.At(Eleven.AddSeconds(33));
        Assert.That(rig.Ideas.Ideas.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose)).OrderBy(idea => idea.Symbol, StringComparer.Ordinal), Is.EqualTo(new[]
        {
            ("BTCUSDT", Eleven, 101m), ("ETHUSDT", Eleven, 101m)
        }), "the feed went quiet with its newest minute recent: it is live");
    }

    /// <summary>
    ///     Task 26.2, confirmed: a break of the stream shorter than the limit of silence — the service restarted, UNAVAILABLE —
    ///     left the feed live, and the replay of the new subscription, by minute and within a minute by pair, was taken by the
    ///     rule of the live feed: a pass between the minute 10:59 of BTCUSDT and that of ETHUSDT computed the candle of 10:00
    ///     of ETHUSDT without it — the idea of BTCUSDT came, that of ETHUSDT did not. After a new subscription the feed is a
    ///     catch-up until it goes quiet; ideas still count without a break since the start.
    /// </summary>
    [Test]
    public async Task A_replay_after_a_break_of_the_stream_gives_the_ideas_of_the_feed_without_the_break()
    {
        (Idea[] whole, _) = await LiveHourOfTen(broken: false);
        Assert.That(whole.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose)), Is.EqualTo(new[]
        {
            ("BTCUSDT", Eleven, 101m), ("ETHUSDT", Eleven, 101m)
        }));

        (Idea[] replayed, DateTimeOffset? since) = await LiveHourOfTen(broken: true);
        Assert.That(
            replayed.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose)).ToArray(),
            Is.EqualTo(whole.Select(idea => (idea.Symbol, idea.Moment, idea.CandleClose)).ToArray()),
            "the same ideas as without the break");
        Assert.That(since, Is.EqualTo(Ten.AddSeconds(5)), "a break shorter than the limit of silence is no break of the ideas");
    }

    // The scanner started at 10:00:05 on the live feed of BTCUSDT and ETHUSDT, a minute a minute closing at 99 up to 10:58;
    // the minutes 10:59 close at 101. Without a break they come at 11:00:01. With one the stream breaks at 10:59:30, and the
    // feed subscribes again at 11:00:20 from 10:53 and replays the minutes by minute and within a minute by pair, with a pass
    // between the minute 10:59 of BTCUSDT and that of ETHUSDT. Gives the ideas and since when they count without a break.
    private static async Task<(Idea[] Ideas, DateTimeOffset? LiveSince)> LiveHourOfTen(bool broken)
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        for (int minute = 0; minute < 59; minute++)
        {
            await CatchUp(rig, Ten.AddMinutes(minute), 1, close: 99);
            await rig.At(Ten.AddMinutes(minute + 1).AddSeconds(1));
        }

        if (broken)
        {
            await rig.At(Ten.AddMinutes(59).AddSeconds(30));
            rig.Time.SetUtcNow(Eleven.AddSeconds(20));
            await rig.Scanner.ApplyAsync(new FeedResubscribed("minutes", Ten.AddMinutes(53)), CancellationToken.None);
            await CatchUp(rig, Ten.AddMinutes(53), 6, close: 99);
            await rig.Scanner.ApplyAsync(Minute("BTCUSDT", Ten.AddMinutes(59), close: 101), CancellationToken.None);
            await rig.At(Eleven.AddSeconds(20), quiet: false);
            Assert.That(rig.Ideas.Ideas, Is.Empty, "the minute 10:59 of ETHUSDT is still to come");

            await rig.Scanner.ApplyAsync(Minute("ETHUSDT", Ten.AddMinutes(59), close: 101), CancellationToken.None);
            await rig.At(Eleven.AddSeconds(21), quiet: false);
            Assert.That(rig.Ideas.Ideas, Is.Empty, "the feed still replays: the batch may not be whole");
            await rig.At(Eleven.AddSeconds(22));
        }
        else
        {
            rig.Time.SetUtcNow(Eleven.AddSeconds(1));
            await CatchUp(rig, Ten.AddMinutes(59), 1, close: 101);
            await rig.At(Eleven.AddSeconds(15));
        }

        return ([.. rig.Ideas.Ideas.OrderBy(idea => idea.Symbol, StringComparer.Ordinal)], rig.Scanner.LiveSince);
    }

    /// <summary>
    ///     Task 26.1, confirmed: a catch-up of three candles — the signal at 11:00 and at 13:00, the idea of 11:00 runs to 14:00
    ///     by its time. In two passes the first walk of the second pass ran the idea through the minutes up to 14:05 and closed
    ///     it «ahead» before the boundary of 13:00 was computed, and the rule of one open idea, looking at the open ones only,
    ///     let a second idea be born while the first one ran. A backtest skips that order: the position is taken.
    /// </summary>
    [Test]
    public async Task A_catch_up_of_three_candles_gives_no_second_idea_while_the_first_one_runs([Values(1, 2)] int passes)
    {
        Idea[] ideas = await CatchUpOfThreeCandles(stop: false, passes);

        Assert.That(ideas.Select(idea => (idea.Moment, idea.ExitTime)), Is.EqualTo(new[] { (Eleven, (DateTimeOffset?)Eleven.AddHours(3)) }), "the order of 13:00 comes in the time of the idea of 11:00");
    }

    /// <summary>
    ///     Task 26.1: the idea of 11:00 reaches its stop in the minute 11:01, before the third candle, so the order of 13:00 is
    ///     a new idea, as in a backtest. In one pass the idea of 11:00 still waits for its write when the boundary of 13:00 is
    ///     computed, and was not walked: its time up to 14:00 stopped the order — the signal fired, the pair has no open idea,
    ///     and no idea came (the two events of 18:45 of the ninth check).
    /// </summary>
    [Test]
    public async Task A_catch_up_of_three_candles_with_a_stop_before_the_third_gives_two_ideas_as_a_backtest([Values(1, 2)] int passes)
    {
        Idea[] ideas = await CatchUpOfThreeCandles(stop: true, passes);

        Assert.That(ideas.Select(idea => (idea.Moment, idea.ExitReason, idea.ExitTime)).First(), Is.EqualTo((Eleven, (ExitReason?)ExitReason.Stop, (DateTimeOffset?)Eleven.AddMinutes(1))));
        Assert.That(ideas.Select(idea => idea.Moment), Is.EqualTo(new[] { Eleven, Eleven.AddHours(2) }), "the idea of 11:00 ended at 11:01");
    }

    // The scanner started at 10:00:05 with close-up for 3h (with a stop of 2% when asked), and the feed that catches up at
    // 14:30 the minutes of BTCUSDT from 10:00 to 14:05: the candles of 10:00–13:00 close at 101, 99, 101 and 99 — the
    // signal at 11:00 and at 13:00 — and the minute 11:01 goes down to 97. In two passes: one after the minute 11:00, one
    // after the minute 14:05; in one pass: after the minute 14:05.
    private static async Task<Idea[]> CatchUpOfThreeCandles(bool stop, int passes)
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "3h", stop: stop ? 0.02m : null);
        await rig.Start();
        DateTimeOffset now = Eleven.AddHours(3).AddMinutes(30);
        rig.Time.SetUtcNow(now);

        await rig.Minutes("BTCUSDT", Ten, 61, close: 101);
        if (passes == 2)
        {
            await rig.At(now.AddSeconds(1), quiet: false);
            Assert.That(rig.Ideas.Ideas.Select(idea => idea.Moment), Is.EqualTo(new[] { Eleven }));
        }

        await rig.Minutes("BTCUSDT", Eleven.AddMinutes(1), 1, close: 99, low: 97);
        await rig.Minutes("BTCUSDT", Eleven.AddMinutes(2), 58, close: 99);
        await rig.Minutes("BTCUSDT", Eleven.AddHours(1), 60, close: 101);
        await rig.Minutes("BTCUSDT", Eleven.AddHours(2), 66, close: 99);
        await rig.At(now.AddSeconds(2), quiet: false);
        return [.. rig.Ideas.Ideas.OrderBy(idea => idea.Moment)];
    }

    // The scanner started at 10:00:05 and the feed that catches up the hour of 10:00 at 11:10, minute by minute and within a
    // minute BTCUSDT before ETHUSDT, then the minute 11:00 of both; with or without a pass between the two minutes 10:59.
    private static async Task<Idea[]> CatchUpAcrossTheBoundary(bool passInTheMiddle)
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "8h");
        await rig.Start();
        rig.Time.SetUtcNow(Eleven.AddMinutes(10));

        await CatchUp(rig, Ten, 59, close: 99);
        await rig.Scanner.ApplyAsync(Minute("BTCUSDT", Ten.AddMinutes(59), close: 101), CancellationToken.None);
        if (passInTheMiddle)
        {
            await rig.At(Eleven.AddMinutes(10).AddSeconds(1), quiet: false);
            Assert.That(rig.Ideas.Ideas, Is.Empty, "the minute 10:59 of ETHUSDT is still to come: the candle of 10:00 is not computed");
        }

        await rig.Scanner.ApplyAsync(Minute("ETHUSDT", Ten.AddMinutes(59), close: 101), CancellationToken.None);
        await CatchUp(rig, Eleven, 1, close: 101);
        await rig.At(Eleven.AddMinutes(10).AddSeconds(2), quiet: false);
        return [.. rig.Ideas.Ideas.OrderBy(idea => idea.Symbol, StringComparer.Ordinal)];
    }

    // Closed minutes of BTCUSDT and ETHUSDT from <paramref name="from" />, told by minute and within a minute by pair.
    private static async Task CatchUp(ScannerRig rig, DateTimeOffset from, int count, decimal close)
    {
        for (int minute = 0; minute < count; minute++)
        {
            foreach (string symbol in new[] { "BTCUSDT", "ETHUSDT" })
            {
                await rig.Scanner.ApplyAsync(Minute(symbol, from.AddMinutes(minute), close), CancellationToken.None);
            }
        }
    }

    private static LiveMinute Minute(string symbol, DateTimeOffset open, decimal close)
    {
        return new LiveMinute(symbol, open, 100, Math.Max(100, close), Math.Min(100, close), close, 1, close);
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
