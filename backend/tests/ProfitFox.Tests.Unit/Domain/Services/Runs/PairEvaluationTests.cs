using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Runs;

/// <summary>
///     Spec strategy-backtest «Заявка при открытой позиции в прогоне» and «Случайные входы» (change add-strategy-graph, task
///     4.2; change fix-backtest-random-control, task 3.1): the orders of a pair through the rule of one position, and its
///     random entries at the moments of the orders of other pairs, in the week of Monday 2026-01-05, on the strategy of H0001
///     with its time, stop and target changed.
/// </summary>
public sealed class PairEvaluationTests
{
    private static readonly DateTimeOffset Monday = Utc(2026, 1, 5);
    private static readonly DateTimeOffset Noon = Utc(2026, 1, 5, 12);
    private static readonly DateTimeOffset FourPm = Utc(2026, 1, 5, 16);
    private static readonly DateTimeOffset EightPm = Utc(2026, 1, 5, 20);
    private static readonly long Week = Blocks.Of(Monday, TimeBlock.Week);

    [Test]
    public void A_second_order_inside_the_time_of_the_first_is_skipped()
    {
        Case run = Design(after: "1d");
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon, FourPm), run.Design);

        IReadOnlyList<EventRow> rows = evaluation.Evaluate(Week, Path(run)).Rows;

        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week }));
        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Noon }), "the order at 12:00 counts, the order at 16:00 does not");
        Assert.That(evaluation.Skipped, Is.EqualTo(1));
        Assert.That(rows[0].Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(rows[0].Side, Is.EqualTo(EntrySide.Short));
        Assert.That(rows[0].Outcome!.ExitTime, Is.EqualTo(Noon.AddDays(1)));
    }

    [Test]
    public void A_stop_before_the_second_order_lets_both_count()
    {
        Case run = Design(after: "1d", stop: 0.02m);
        // The short from 100 is stopped in the minute of 12:07, where the high is 103.
        MinutePath path = Path(run, (Noon.AddMinutes(7), High: 103, Low: 100));
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon, FourPm), run.Design);

        IReadOnlyList<EventRow> rows = evaluation.Evaluate(Week, path).Rows;

        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Noon, FourPm }));
        Assert.That(evaluation.Skipped, Is.Zero);
        Assert.That((rows[0].Outcome!.ExitReason, rows[0].Outcome!.ExitTime), Is.EqualTo((ExitReason.Stop, (DateTimeOffset?)Noon.AddMinutes(7))));
        Assert.That(rows[1].Status, Is.EqualTo(TradeStatus.Ok));
    }

    [Test]
    public void An_order_with_a_trade_left_out_keeps_its_row_and_does_not_hold_the_pair()
    {
        Case run = Design(after: "1d");
        // A hole in the minutes at 12:30 leaves out the trade of 12:00; the trade of 16:00 starts after it.
        MinutePath path = PathWithHole(run, Noon.AddMinutes(30), Noon.AddMinutes(31));
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon, FourPm), run.Design);

        IReadOnlyList<EventRow> rows = evaluation.Evaluate(Week, path).Rows;

        Assert.That(rows.Select(row => (row.Moment, row.Status)), Is.EqualTo(new[] { (Noon, TradeStatus.Coverage), (FourPm, TradeStatus.Ok) }));
        Assert.That(evaluation.Skipped, Is.Zero, "a trade left out does not hold the pair, and its order is not a skipped one");
        Assert.That(rows[0].Event, Is.Null, "an order whose trade is left out is in no statistic");
    }

    [Test]
    public void A_pair_of_the_group_takes_a_random_entry_at_the_moment_of_an_order_of_another_pair_with_its_own_exit_values()
    {
        // «Тот же момент на других парах»: a long of SOLUSDT at 12:00, the group of 20 pairs without SOLUSDT, none with an order
        // at 12:00.
        Case run = Design(after: "1d", stop: 0.02m, target: 0.04m);
        string[] group = [.. Enumerable.Range(1, 20).Select(number => $"P{number:D2}USDT")];
        SetControl control = Control(group, ("SOLUSDT", Noon, EntrySide.Long));
        MinutePath path = Path(run, [.. Minutes(Monday.AddDays(2), Monday.AddDays(4), price: 95)]);
        var plan = new TradePlan(TradeSide.Long, TimeSpan.FromDays(1), stop: 0.02, target: 0.04);

        RandomTrade[] randoms =
        [
            .. group.SelectMany(pair =>
            {
                var evaluation = new PairEvaluation(pair, Steps(run), run.Design, control);
                return evaluation.Blocks.SelectMany(week => evaluation.Evaluate(week, path).Randoms);
            })
        ];

        Assert.That(randoms, Has.Length.EqualTo(20), "one random entry on each pair of the group");
        Assert.That(randoms.Select(random => random.Instrument), Is.EqualTo(group));
        Assert.That(randoms, Has.All.Matches<RandomTrade>(random => random.Entry == Noon && random.Side == EntrySide.Long));
        Assert.That(randoms.Select(random => random.Outcome), Has.All.EqualTo(CycleRun.Simulate(path, Noon, plan, run.Design.Costs, amount: 100).ToTrade()));
    }

    [Test]
    public void A_pair_with_an_order_at_the_moment_takes_no_random_entry_there()
    {
        // «Момент заявки не берётся»: ETHUSDT of the group has an order at 12:00 as well.
        Case run = Design();
        SetControl control = Control(["ETHUSDT", "XRPUSDT"], ("SOLUSDT", Noon, EntrySide.Short), ("ETHUSDT", Noon, EntrySide.Short));

        var eth = new PairEvaluation("ETHUSDT", Steps(run, Noon), run.Design, control);
        var xrp = new PairEvaluation("XRPUSDT", Steps(run), run.Design, control);

        Assert.That(eth.Evaluate(Week, Path(run)).Randoms, Is.Empty);
        Assert.That(xrp.Evaluate(Week, Path(run)).Randoms.Select(random => random.Entry), Is.EqualTo(new[] { Noon }), "the moments of both orders are one moment and side");
    }

    [Test]
    public void A_random_entry_while_the_position_of_the_pair_is_open_is_a_cycle_of_its_own()
    {
        // «Случайный вход при открытой позиции»: the cycle of ETHUSDT runs from 08:00 to 08:00, the order of SOLUSDT comes at 12:00.
        Case run = Design(after: "1d");
        DateTimeOffset eight = Utc(2026, 1, 5, 8);
        SetControl control = Control(["ETHUSDT"], ("SOLUSDT", Noon, EntrySide.Short), ("ETHUSDT", eight, EntrySide.Short));
        MinutePath path = Path(run);

        PairWeek alone = new PairEvaluation("ETHUSDT", Steps(run, eight), run.Design).Evaluate(Week, path);
        PairWeek week = new PairEvaluation("ETHUSDT", Steps(run, eight), run.Design, control).Evaluate(Week, path);

        RandomTrade random = week.Randoms.Single();
        Assert.That((week.Rows.Single().Outcome!.EntryTime, week.Rows.Single().Outcome!.ExitTime), Is.EqualTo(((DateTimeOffset?)eight, (DateTimeOffset?)eight.AddDays(1))));
        Assert.That(week.Rows, Is.EqualTo(alone.Rows), "the cycle of the order of ETHUSDT does not change");
        Assert.That(random.Outcome.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That((random.Outcome.EntryTime, random.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)Noon, (DateTimeOffset?)Noon.AddDays(1))));
        Assert.That(random.Outcome, Is.EqualTo(CycleRun.Simulate(path, Noon, run.Plan, run.Design.Costs, amount: 100).ToTrade()));
    }

    [Test]
    public void The_pair_of_an_order_in_its_own_group_takes_no_random_entry_for_it()
    {
        // «Пара заявки в своей группе»: SOLUSDT is in the group of 20 pairs and has an order this week.
        Case run = Design();
        string[] group = ["SOLUSDT", .. Enumerable.Range(1, 19).Select(number => $"P{number:D2}USDT")];
        SetControl control = Control(group, ("SOLUSDT", Noon, EntrySide.Short));

        RandomTrade[] randoms =
        [
            .. group.SelectMany(pair =>
            {
                var evaluation = new PairEvaluation(pair, pair == "SOLUSDT" ? Steps(run, Noon) : Steps(run), run.Design, control);
                return evaluation.Blocks.SelectMany(week => evaluation.Evaluate(week, Path(run)).Randoms);
            })
        ];

        Assert.That(randoms, Has.Length.EqualTo(19));
        Assert.That(randoms.Select(random => random.Instrument), Has.None.EqualTo("SOLUSDT"));
    }

    [Test]
    public void A_pair_outside_the_group_takes_no_random_entries()
    {
        Case run = Design();
        SetControl control = Control(["ETHUSDT"], ("SOLUSDT", Noon, EntrySide.Short));

        var evaluation = new PairEvaluation("XRPUSDT", Steps(run), run.Design, control);

        Assert.That(evaluation.Blocks, Is.Empty);
    }

    [Test]
    public void A_week_without_orders_of_the_pair_gives_its_random_entries_and_no_rows()
    {
        Case run = Design();
        SetControl control = Control(["ETHUSDT"], ("SOLUSDT", Noon, EntrySide.Short), ("SOLUSDT", FourPm, EntrySide.Short));
        var evaluation = new PairEvaluation("ETHUSDT", Steps(run), run.Design, control);

        PairWeek week = evaluation.Evaluate(Week, Path(run));

        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week }));
        Assert.That(week.Rows, Is.Empty);
        Assert.That(week.Randoms.Select(random => (random.Instrument, random.Entry, random.Side)), Is.EqualTo(new[] { ("ETHUSDT", Noon, EntrySide.Short), ("ETHUSDT", FourPm, EntrySide.Short) }));
    }

    [Test]
    public void A_random_entry_whose_trade_ends_after_the_end_of_history_is_not_taken()
    {
        Case run = Design(after: "1d");
        BacktestDesign design = run.Design with { End = Utc(2026, 1, 6, 6) };
        SetControl control = Control(["ETHUSDT"], ("SOLUSDT", Utc(2026, 1, 5, 4), EntrySide.Short), ("SOLUSDT", Noon, EntrySide.Short));

        var evaluation = new PairEvaluation("ETHUSDT", Steps(run), design, control);

        Assert.That(evaluation.Evaluate(Week, Path(run)).Randoms.Select(random => random.Entry), Is.EqualTo(new[] { Utc(2026, 1, 5, 4) }), "the trade of 12:00 ends on Tuesday 12:00");
    }

    [Test]
    public void The_position_goes_from_one_week_to_the_next()
    {
        Case run = Design(after: "1d");
        DateTimeOffset sundayEvening = Utc(2026, 1, 11, 20);
        DateTimeOffset nextMonday = Utc(2026, 1, 12, 8);
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, sundayEvening, nextMonday), run.Design);

        IReadOnlyList<EventRow> firstWeek = evaluation.Evaluate(Week, Path(run, Week)).Rows;
        IReadOnlyList<EventRow> secondWeek = evaluation.Evaluate(Week + 1, Path(run, Week + 1)).Rows;

        Assert.That(firstWeek.Select(row => row.Moment), Is.EqualTo(new[] { sundayEvening }));
        Assert.That(secondWeek, Is.Empty, "the cycle of Sunday 20:00 runs to Monday 20:00");
        Assert.That(evaluation.Skipped, Is.EqualTo(1));
    }

    [Test]
    public void Weeks_are_evaluated_in_the_order_of_time()
    {
        Case run = Design();
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon, Utc(2026, 1, 14, 4)), run.Design);
        evaluation.Evaluate(Week + 1, Path(run, Week + 1));

        Assert.That(() => evaluation.Evaluate(Week, Path(run, Week)), Throws.InvalidOperationException);
    }

    // The strategy of H0001 with the time, stop and target given, on exploration with the random entries given.
    private static Case Design(string after = "4h", decimal? stop = null, decimal? target = null, int randoms = 20)
    {
        TradingStrategy strategy = Strategy(after, stop, target);
        StrategyStep written = new StrategyExecutor(strategy).BeforeCandles(DateTimeOffset.UnixEpoch);
        TradePlan plan = EntryPlan.Of(written, EntrySide.Short)!;
        return new Case(Exploration(randoms), strategy, plan);
    }

    // The orders of the set over the pairs, and a control group of the given pairs, all of them with history.
    private static SetControl Control(string[] group, params (string Symbol, DateTimeOffset Moment, EntrySide Side)[] orders)
    {
        var byPair = orders
            .GroupBy(order => order.Symbol)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PairOrder>)[.. pair.Select(order => new PairOrder(order.Moment, order.Side))]);
        var groups = new ControlGroups(group.ToDictionary(symbol => symbol, _ => Utc(2025, 1, 6)), TimeBlock.Week, size: 20);
        return new SetControl(new SetOrders(byPair, TimeBlock.Week), groups);
    }

    // The steps of the strategy with orders at the events: its exit values are written, so every moment has them.
    private static PairSteps Steps(Case run, params DateTimeOffset[] events)
    {
        StrategyStep written = new StrategyExecutor(run.Strategy).BeforeCandles(DateTimeOffset.UnixEpoch);
        EntrySide side = run.Strategy.Entries.Single().Side;
        return new PairSteps([.. events.Select(moment => written with { Moment = moment, Entry = side })], written);
    }

    private static MinutePath Path(Case run, params (DateTimeOffset Minute, double High, double Low)[] changed)
    {
        return Path(run, Week, changed);
    }

    // The minutes of a week as a backtest reads them, at a price of 100 but for the changed ones.
    private static MinutePath Path(Case run, long block, params (DateTimeOffset Minute, double High, double Low)[] changed)
    {
        (DateTimeOffset from, DateTimeOffset to) = run.Design.Span(block, run.Plan.Horizon);
        int length = (int)(to - from).TotalMinutes;
        double[] open = [.. Enumerable.Repeat(100.0, length)];
        double[] high = [.. Enumerable.Repeat(100.0, length)];
        double[] low = [.. Enumerable.Repeat(100.0, length)];
        double[] close = [.. Enumerable.Repeat(100.0, length)];
        foreach ((DateTimeOffset minute, double highPrice, double lowPrice) in changed)
        {
            int index = (int)(minute - from).TotalMinutes;
            high[index] = highPrice;
            low[index] = lowPrice;
            if (highPrice == lowPrice)
            {
                open[index] = highPrice;
                close[index] = highPrice;
            }
        }

        return new MinutePath(from, open, high, low, close, [.. Enumerable.Repeat(true, length)], [], from, to.AddDays(1));
    }

    // The minutes of the week at a price of 100, with no history in [from, to).
    private static MinutePath PathWithHole(Case run, DateTimeOffset holeFrom, DateTimeOffset holeTo)
    {
        (DateTimeOffset from, DateTimeOffset to) = run.Design.Span(Week, run.Plan.Horizon);
        int length = (int)(to - from).TotalMinutes;
        double[] Prices() => [.. Enumerable.Repeat(100.0, length)];
        bool[] covered = [.. Enumerable.Range(0, length).Select(index => from.AddMinutes(index) < holeFrom || from.AddMinutes(index) >= holeTo)];

        return new MinutePath(from, Prices(), Prices(), Prices(), Prices(), covered, [], from, to.AddDays(1));
    }

    // Every minute of [from, to) at one price.
    private static IEnumerable<(DateTimeOffset Minute, double High, double Low)> Minutes(DateTimeOffset from, DateTimeOffset to, double price)
    {
        for (DateTimeOffset minute = from; minute < to; minute += TimeSpan.FromMinutes(1))
        {
            yield return (minute, price, price);
        }
    }

    // A strategy, its design and the plan its orders give.
    private sealed record Case(BacktestDesign Design, TradingStrategy Strategy, TradePlan Plan);
}