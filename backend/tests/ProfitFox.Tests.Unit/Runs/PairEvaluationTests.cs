using System.Text.Json.Nodes;
using ProfitFox.Domain.Hypotheses;
using ProfitFox.Domain.Runs;
using ProfitFox.Domain.Strategies;
using ProfitFox.Domain.Positions;
using ProfitFox.Domain.Trades;
using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;
using ProfitFox.Tests.Unit.Hypotheses;

using static ProfitFox.Tests.Unit.Runs.Designs;

namespace ProfitFox.Tests.Unit.Runs;

/// <summary>
///     Spec hypothesis-bench «Событие при открытой позиции», «Случайные входы» and «Гипотеза исполняется как стратегия»
///     (change add-strategy-graph, task 4.2): the events of a pair through the rule of one position, with their random
///     entries, in the week of Monday 2026-01-05.
/// </summary>
public sealed class PairEvaluationTests
{
    private static readonly DateTimeOffset Monday = Utc(2026, 1, 5);
    private static readonly DateTimeOffset Noon = Utc(2026, 1, 5, 12);
    private static readonly DateTimeOffset FourPm = Utc(2026, 1, 5, 16);
    private static readonly DateTimeOffset EightPm = Utc(2026, 1, 5, 20);
    private static readonly long Week = Blocks.Of(Monday, TimeBlock.Week);

    [Test]
    public void A_second_event_inside_the_time_of_the_first_is_skipped()
    {
        RunDesign design = Design(plan => plan["horizon"] = "1d");
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, Noon, FourPm), design, withEventOutcomes: true);

        List<EventRow> rows = evaluation.Evaluate(Week, Path(design));

        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week }));
        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Noon }), "the event at 12:00 counts, the event at 16:00 does not");
        Assert.That(evaluation.Skipped, Is.EqualTo(1));
        Assert.That(rows[0].Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(rows[0].Outcome!.ExitTime, Is.EqualTo(Noon.AddDays(1)));
        Assert.That(rows[0].Randoms, Has.Count.EqualTo(20), "only the event that opened a cycle has random entries");
    }

    [Test]
    public void A_stop_before_the_second_event_lets_both_count()
    {
        RunDesign design = Design(plan =>
        {
            plan["horizon"] = "1d";
            plan["stop"] = JsonNode.Parse("0.02");
        });
        // The short from 100 is stopped in the minute of 12:07, where the high is 103.
        MinutePath path = Path(design, (Noon.AddMinutes(7), High: 103, Low: 100));
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, Noon, FourPm), design, withEventOutcomes: true);

        List<EventRow> rows = evaluation.Evaluate(Week, path);

        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Noon, FourPm }));
        Assert.That(evaluation.Skipped, Is.Zero);
        Assert.That((rows[0].Outcome!.ExitReason, rows[0].Outcome!.ExitTime), Is.EqualTo((ExitReason.Stop, (DateTimeOffset?)Noon.AddMinutes(7))));
        Assert.That(rows[1].Status, Is.EqualTo(TradeStatus.Ok));
    }

    [Test]
    public void An_event_with_a_trade_left_out_keeps_its_row_and_its_random_entries_and_does_not_hold_the_pair()
    {
        RunDesign design = Design(plan => plan["horizon"] = "1d");
        // A hole in the minutes at 12:30 leaves out the trade of 12:00; the trade of 16:00 starts after it.
        MinutePath path = PathWithHole(design, Noon.AddMinutes(30), Noon.AddMinutes(31));
        DateTimeOffset[] events = [Noon, FourPm];
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, events), design, withEventOutcomes: true);

        List<EventRow> rows = evaluation.Evaluate(Week, path);

        var randoms = new RandomEntries("BTCUSDT", events, design);
        IReadOnlyList<DateTimeOffset> candidates = randoms.Candidates(Week);
        DateTimeOffset[][] expected = [randoms.Draw(candidates), randoms.Draw(candidates)];
        Assert.That(rows.Select(row => (row.Moment, row.Status)), Is.EqualTo(new[] { (Noon, TradeStatus.Coverage), (FourPm, TradeStatus.Ok) }));
        Assert.That(evaluation.Skipped, Is.Zero, "a trade left out does not hold the pair, and its event is not a skipped one");
        Assert.That(rows[0].Event, Is.Null, "an event whose trade is left out is in no statistic");
        Assert.That(
            rows.Select(row => row.Randoms.Select(random => random.Entry).ToArray()),
            Is.EqualTo(expected),
            "the generator is asked for both events, as under engine version 1");
    }

    [Test]
    public void The_moment_of_a_skipped_event_is_not_a_random_entry()
    {
        RunDesign design = Design(plan => plan["horizon"] = "1d", randomsPerEvent: 200);
        PairSteps steps = Steps(design, Noon, FourPm);
        var evaluation = new PairEvaluation("BTCUSDT", steps, design, withEventOutcomes: true);

        List<EventRow> rows = evaluation.Evaluate(Week, Path(design));

        IReadOnlyList<DateTimeOffset> candidates = new RandomEntries("BTCUSDT", [Noon, FourPm], design).Candidates(Week);
        DateTimeOffset[] entries = [.. rows.Single().Randoms.Select(random => random.Entry)];
        Assert.That(evaluation.Skipped, Is.EqualTo(1), "the event at 16:00 is skipped");
        Assert.That(candidates, Has.None.EqualTo(FourPm).And.None.EqualTo(Noon));
        Assert.That(entries, Has.Length.EqualTo(200));
        Assert.That(entries, Has.None.EqualTo(FourPm).And.None.EqualTo(Noon));
        Assert.That(entries, Has.All.Matches<DateTimeOffset>(candidates.Contains));
        Assert.That(entries.Distinct().Count(), Is.EqualTo(candidates.Count), "with 200 draws every other close of the week is taken");
    }

    [Test]
    public void A_random_entry_while_the_position_is_open_is_a_cycle_of_its_own()
    {
        RunDesign design = Design(plan => plan["horizon"] = "1d", randomsPerEvent: 200);
        MinutePath path = Path(design);
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, Noon), design, withEventOutcomes: true);

        EventRow row = evaluation.Evaluate(Week, path).Single();

        RandomTrade random = row.Randoms.First(trade => trade.Entry == EightPm);
        Assert.That((row.Outcome!.EntryTime, row.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)Noon, (DateTimeOffset?)Noon.AddDays(1))), "the cycle of the event runs from 12:00 to 12:00");
        Assert.That(random.Outcome.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That((random.Outcome.EntryTime, random.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)EightPm, (DateTimeOffset?)EightPm.AddDays(1))));
        Assert.That(random.Outcome, Is.EqualTo(TradeSimulator.Simulate(path, EightPm, design.Hypothesis.Plan, design.Hypothesis.Costs)));
    }

    [Test]
    public void The_cycle_of_an_event_and_every_random_entry_carry_the_stop_and_the_target_of_the_plan()
    {
        RunDesign design = Design(plan =>
        {
            plan["horizon"] = "1d";
            plan["stop"] = JsonNode.Parse("0.02");
            plan["target"] = JsonNode.Parse("0.04");
        });
        // The price falls by 5 % on Wednesday and comes back by 4 % on Friday: shorts before the fall reach the target, shorts
        // before the rise are stopped.
        MinutePath path = Path(design, [.. Minutes(Monday.AddDays(2), Monday.AddDays(4), price: 95), .. Minutes(Monday.AddDays(4), Monday.AddDays(8), price: 98.8)]);
        PairSteps steps = Steps(design, Noon);
        var evaluation = new PairEvaluation("BTCUSDT", steps, design, withEventOutcomes: true);

        EventRow row = evaluation.Evaluate(Week, path).Single();

        var plan = new TradePlan(TradeSide.Short, TimeSpan.FromDays(1), stop: 0.02, target: 0.04);
        Assert.That(design.Hypothesis.Plan, Is.EqualTo(plan));
        Assert.That(EntryPlan.Of(steps.Orders.Single(), EntrySide.Short), Is.EqualTo(plan), "the order carries a stop of exactly 0.02 and a target of exactly 0.04");
        Assert.That(row.Outcome, Is.EqualTo(TradeSimulator.Simulate(path, Noon, plan, design.Hypothesis.Costs)));
        Assert.That(row.Randoms, Has.Count.EqualTo(20));
        Assert.That(row.Randoms.Select(random => EntryPlan.Of(steps.At(random.Entry), EntrySide.Short)), Has.All.EqualTo(plan));
        Assert.That(
            row.Randoms.Select(random => random.Outcome),
            Is.EqualTo(row.Randoms.Select(random => TradeSimulator.Simulate(path, random.Entry, plan, design.Hypothesis.Costs))));
        Assert.That(row.Randoms.Select(random => random.Outcome.ExitReason).Distinct(), Has.Some.EqualTo(ExitReason.Target).Or.Some.EqualTo(ExitReason.Stop));
    }

    [Test]
    public void Without_overlap_the_random_entries_are_drawn_block_by_block_and_event_by_event_from_one_generator()
    {
        RunDesign design = Design(_ => { });
        DateTimeOffset[] events = [Utc(2026, 1, 5, 8), Utc(2026, 1, 5, 16), Utc(2026, 1, 14, 4)];
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, events), design, withEventOutcomes: true);

        List<EventRow> rows = [.. evaluation.Blocks.SelectMany(block => evaluation.Evaluate(block, Path(design, block)))];

        var randoms = new RandomEntries("BTCUSDT", events, design);
        IReadOnlyList<DateTimeOffset> first = randoms.Candidates(Week);
        IReadOnlyList<DateTimeOffset> second = randoms.Candidates(Week + 1);
        DateTimeOffset[][] expected = [randoms.Draw(first), randoms.Draw(first), randoms.Draw(second)];
        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week, Week + 1 }));
        Assert.That(evaluation.Skipped, Is.Zero);
        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(events));
        Assert.That(rows.Select(row => row.Randoms.Select(random => random.Entry).ToArray()), Is.EqualTo(expected));
    }

    [Test]
    public void The_position_goes_from_one_block_to_the_next()
    {
        RunDesign design = Design(plan => plan["horizon"] = "1d");
        DateTimeOffset sundayEvening = Utc(2026, 1, 11, 20);
        DateTimeOffset nextMonday = Utc(2026, 1, 12, 8);
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, sundayEvening, nextMonday), design, withEventOutcomes: true);

        List<EventRow> firstWeek = evaluation.Evaluate(Week, Path(design, Week));
        List<EventRow> secondWeek = evaluation.Evaluate(Week + 1, Path(design, Week + 1));

        Assert.That(firstWeek.Select(row => row.Moment), Is.EqualTo(new[] { sundayEvening }));
        Assert.That(secondWeek, Is.Empty, "the cycle of Sunday 20:00 runs to Monday 20:00");
        Assert.That(evaluation.Skipped, Is.EqualTo(1));
    }

    [Test]
    public void Without_the_outcomes_of_events_the_same_events_are_skipped_and_no_outcome_is_computed()
    {
        RunDesign design = Design(plan =>
        {
            plan["horizon"] = "1d";
            plan["stop"] = JsonNode.Parse("0.02");
        });
        MinutePath path = Path(design, (Noon.AddMinutes(7), High: 103, Low: 100));
        DateTimeOffset[] events = [Noon, FourPm, EightPm];
        var seeing = new PairEvaluation("BTCUSDT", Steps(design, events), design, withEventOutcomes: true);
        var blind = new PairEvaluation("BTCUSDT", Steps(design, events), design, withEventOutcomes: false);

        List<EventRow> seen = seeing.Evaluate(Week, path);
        List<EventRow> unseen = blind.Evaluate(Week, path);

        Assert.That(unseen.Select(row => row.Moment), Is.EqualTo(new[] { Noon, FourPm }).And.EqualTo(seen.Select(row => row.Moment)));
        Assert.That((blind.Skipped, seeing.Skipped), Is.EqualTo((1, 1)), "the stop of 12:07 frees the pair for 16:00, which holds it at 20:00");
        Assert.That(unseen, Has.All.Matches<EventRow>(row => row.Outcome is null && row.Event is null));
        Assert.That(unseen.Select(row => (row.Status, row.RandomValid, row.PseudoDifference)), Is.EqualTo(seen.Select(row => (row.Status, row.RandomValid, row.PseudoDifference))));
    }

    [Test]
    public void Blocks_are_evaluated_in_the_order_of_time()
    {
        RunDesign design = Design(_ => { });
        var evaluation = new PairEvaluation("BTCUSDT", Steps(design, Noon, Utc(2026, 1, 14, 4)), design, withEventOutcomes: true);
        evaluation.Evaluate(Week + 1, Path(design, Week + 1));

        Assert.That(() => evaluation.Evaluate(Week, Path(design, Week)), Throws.InvalidOperationException);
    }

    // H0001 with its plan changed, on exploration.
    private static RunDesign Design(Action<JsonNode> plan, int randomsPerEvent = 20)
    {
        JsonObject file = JsonNode.Parse(HypothesisReaderTests.H0001Text)!.AsObject();
        file["randomsPerEvent"] = randomsPerEvent;
        plan(file["plan"]!);
        return Exploration(HypothesisReader.Parse(file.ToJsonString()));
    }

    // The steps of the strategy of the hypothesis with orders at the events: its exit values are written, so every moment
    // has them.
    private static PairSteps Steps(RunDesign design, params DateTimeOffset[] events)
    {
        StrategyStep written = new StrategyExecutor(design.Hypothesis.Strategy).BeforeCandles(DateTimeOffset.UnixEpoch);
        EntrySide side = design.Hypothesis.Strategy.Entries.Single().Side;
        return new PairSteps([.. events.Select(moment => written with { Moment = moment, Entry = side })], written);
    }

    private static MinutePath Path(RunDesign design, params (DateTimeOffset Minute, double High, double Low)[] changed)
    {
        return Path(design, Week, changed);
    }

    // The minutes of a block as a run reads them, at a price of 100 but for the changed ones.
    private static MinutePath Path(RunDesign design, long block, params (DateTimeOffset Minute, double High, double Low)[] changed)
    {
        (DateTimeOffset from, DateTimeOffset to) = design.Span(block);
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
    private static MinutePath PathWithHole(RunDesign design, DateTimeOffset holeFrom, DateTimeOffset holeTo)
    {
        (DateTimeOffset from, DateTimeOffset to) = design.Span(Week);
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
}
