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
///     4.2): the orders of a pair through the rule of one position, with their random entries, in the week of Monday
///     2026-01-05, on the strategy of H0001 with its time, stop and target changed.
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

        List<EventRow> rows = evaluation.Evaluate(Week, Path(run));

        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week }));
        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Noon }), "the order at 12:00 counts, the order at 16:00 does not");
        Assert.That(evaluation.Skipped, Is.EqualTo(1));
        Assert.That(rows[0].Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(rows[0].Outcome!.ExitTime, Is.EqualTo(Noon.AddDays(1)));
        Assert.That(rows[0].Randoms, Has.Count.EqualTo(20), "only the order that opened a cycle has random entries");
    }

    [Test]
    public void A_stop_before_the_second_order_lets_both_count()
    {
        Case run = Design(after: "1d", stop: 0.02m);
        // The short from 100 is stopped in the minute of 12:07, where the high is 103.
        MinutePath path = Path(run, (Noon.AddMinutes(7), High: 103, Low: 100));
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon, FourPm), run.Design);

        List<EventRow> rows = evaluation.Evaluate(Week, path);

        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Noon, FourPm }));
        Assert.That(evaluation.Skipped, Is.Zero);
        Assert.That((rows[0].Outcome!.ExitReason, rows[0].Outcome!.ExitTime), Is.EqualTo((ExitReason.Stop, (DateTimeOffset?)Noon.AddMinutes(7))));
        Assert.That(rows[1].Status, Is.EqualTo(TradeStatus.Ok));
    }

    [Test]
    public void An_order_with_a_trade_left_out_keeps_its_row_and_its_random_entries_and_does_not_hold_the_pair()
    {
        Case run = Design(after: "1d");
        // A hole in the minutes at 12:30 leaves out the trade of 12:00; the trade of 16:00 starts after it.
        MinutePath path = PathWithHole(run, Noon.AddMinutes(30), Noon.AddMinutes(31));
        DateTimeOffset[] events = [Noon, FourPm];
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, events), run.Design);

        List<EventRow> rows = evaluation.Evaluate(Week, path);

        var randoms = new RandomEntries("BTCUSDT", events, run.Design, _ => run.Plan.Horizon);
        IReadOnlyList<DateTimeOffset> candidates = randoms.Candidates(Week);
        DateTimeOffset[][] expected = [randoms.Draw(candidates), randoms.Draw(candidates)];
        Assert.That(rows.Select(row => (row.Moment, row.Status)), Is.EqualTo(new[] { (Noon, TradeStatus.Coverage), (FourPm, TradeStatus.Ok) }));
        Assert.That(evaluation.Skipped, Is.Zero, "a trade left out does not hold the pair, and its order is not a skipped one");
        Assert.That(rows[0].Event, Is.Null, "an order whose trade is left out is in no statistic");
        Assert.That(
            rows.Select(row => row.Randoms.Select(random => random.Entry).ToArray()),
            Is.EqualTo(expected),
            "the generator is asked for both orders, as under engine version 1");
    }

    [Test]
    public void The_moment_of_a_skipped_order_is_not_a_random_entry()
    {
        Case run = Design(after: "1d", randoms: 200);
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon, FourPm), run.Design);

        List<EventRow> rows = evaluation.Evaluate(Week, Path(run));

        IReadOnlyList<DateTimeOffset> candidates = new RandomEntries("BTCUSDT", [Noon, FourPm], run.Design, _ => run.Plan.Horizon).Candidates(Week);
        DateTimeOffset[] entries = [.. rows.Single().Randoms.Select(random => random.Entry)];
        Assert.That(evaluation.Skipped, Is.EqualTo(1), "the order at 16:00 is skipped");
        Assert.That(candidates, Has.None.EqualTo(FourPm).And.None.EqualTo(Noon));
        Assert.That(entries, Has.Length.EqualTo(200));
        Assert.That(entries, Has.None.EqualTo(FourPm).And.None.EqualTo(Noon));
        Assert.That(entries, Has.All.Matches<DateTimeOffset>(candidates.Contains));
        Assert.That(entries.Distinct().Count(), Is.EqualTo(candidates.Count), "with 200 draws every other close of the week is taken");
    }

    [Test]
    public void A_random_entry_while_the_position_is_open_is_a_cycle_of_its_own()
    {
        Case run = Design(after: "1d", randoms: 200);
        MinutePath path = Path(run);
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, Noon), run.Design);

        EventRow row = evaluation.Evaluate(Week, path).Single();

        RandomTrade random = row.Randoms.First(trade => trade.Entry == EightPm);
        Assert.That((row.Outcome!.EntryTime, row.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)Noon, (DateTimeOffset?)Noon.AddDays(1))), "the cycle of the order runs from 12:00 to 12:00");
        Assert.That(random.Outcome.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That((random.Outcome.EntryTime, random.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)EightPm, (DateTimeOffset?)EightPm.AddDays(1))));
        Assert.That(random.Outcome, Is.EqualTo(CycleRun.Simulate(path, EightPm, run.Plan, run.Design.Costs, amount: 100).ToTrade()));
    }

    [Test]
    public void The_cycle_of_an_order_and_every_random_entry_carry_the_stop_and_the_target_of_the_strategy()
    {
        Case run = Design(after: "1d", stop: 0.02m, target: 0.04m);
        // The price falls by 5 % on Wednesday and comes back by 4 % on Friday: shorts before the fall reach the target, shorts
        // before the rise are stopped.
        MinutePath path = Path(run, [.. Minutes(Monday.AddDays(2), Monday.AddDays(4), price: 95), .. Minutes(Monday.AddDays(4), Monday.AddDays(8), price: 98.8)]);
        PairSteps steps = Steps(run, Noon);
        var evaluation = new PairEvaluation("BTCUSDT", steps, run.Design);

        EventRow row = evaluation.Evaluate(Week, path).Single();

        var plan = new TradePlan(TradeSide.Short, TimeSpan.FromDays(1), stop: 0.02, target: 0.04);
        Assert.That(run.Plan, Is.EqualTo(plan));
        Assert.That(EntryPlan.Of(steps.Orders.Single(), EntrySide.Short), Is.EqualTo(plan), "the order carries a stop of exactly 0.02 and a target of exactly 0.04");
        Assert.That(row.Outcome, Is.EqualTo(CycleRun.Simulate(path, Noon, plan, run.Design.Costs, amount: 100).ToTrade()));
        Assert.That(row.Randoms, Has.Count.EqualTo(20));
        Assert.That(row.Randoms.Select(random => EntryPlan.Of(steps.At(random.Entry), EntrySide.Short)), Has.All.EqualTo(plan));
        Assert.That(
            row.Randoms.Select(random => random.Outcome),
            Is.EqualTo(row.Randoms.Select(random => CycleRun.Simulate(path, random.Entry, plan, run.Design.Costs, amount: 100).ToTrade())));
        Assert.That(row.Randoms.Select(random => random.Outcome.ExitReason).Distinct(), Has.Some.EqualTo(ExitReason.Target).Or.Some.EqualTo(ExitReason.Stop));
    }

    [Test]
    public void Without_overlap_the_random_entries_are_drawn_week_by_week_and_order_by_order_from_one_generator()
    {
        Case run = Design();
        DateTimeOffset[] events = [Utc(2026, 1, 5, 8), Utc(2026, 1, 5, 16), Utc(2026, 1, 14, 4)];
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, events), run.Design);

        List<EventRow> rows = [.. evaluation.Blocks.SelectMany(block => evaluation.Evaluate(block, Path(run, block)))];

        var randoms = new RandomEntries("BTCUSDT", events, run.Design, _ => run.Plan.Horizon);
        IReadOnlyList<DateTimeOffset> first = randoms.Candidates(Week);
        IReadOnlyList<DateTimeOffset> second = randoms.Candidates(Week + 1);
        DateTimeOffset[][] expected = [randoms.Draw(first), randoms.Draw(first), randoms.Draw(second)];
        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week, Week + 1 }));
        Assert.That(evaluation.Skipped, Is.Zero);
        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(events));
        Assert.That(rows.Select(row => row.Randoms.Select(random => random.Entry).ToArray()), Is.EqualTo(expected));
    }

    [Test]
    public void The_position_goes_from_one_week_to_the_next()
    {
        Case run = Design(after: "1d");
        DateTimeOffset sundayEvening = Utc(2026, 1, 11, 20);
        DateTimeOffset nextMonday = Utc(2026, 1, 12, 8);
        var evaluation = new PairEvaluation("BTCUSDT", Steps(run, sundayEvening, nextMonday), run.Design);

        List<EventRow> firstWeek = evaluation.Evaluate(Week, Path(run, Week));
        List<EventRow> secondWeek = evaluation.Evaluate(Week + 1, Path(run, Week + 1));

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
