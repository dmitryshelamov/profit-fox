using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Positions;

/// <summary>
///     Spec strategy-position (change add-strategy-graph, tasks 3.1 and 3.3): the cycle of an entry order, one position
///     on a pair, an order that is not allowed, a trade left out, and a cycle opened outside the graph. The paths start at
///     12:00 on Monday 2026-01-05 (<see cref="Paths.Start" />).
/// </summary>
public sealed class PairPositionTests
{
    private const int Hour = 60;
    private const int Day = 24 * Hour;
    private static readonly TradeCosts Costs = TradeCosts.Default;
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private static readonly DateTimeOffset Noon = Paths.Start;
    private static readonly DateTimeOffset FourPm = Paths.At(4 * Hour);
    private static readonly DateTimeOffset EightPm = Paths.At(8 * Hour);
    private static readonly DateTimeOffset NoonTomorrow = Paths.At(Day);

    [Test]
    public void The_cycle_of_H0001_is_the_trade_of_its_plan()
    {
        MinutePath path = new Paths().Flat(100, 100).Flat(99, 3 * Day).Payment(2 * Hour, 0.0006).Build();
        var position = new PairPosition();

        Cycle? cycle = position.Enter(path, Order(Noon, EntrySide.Short, FourHours), Costs);

        TradeOutcome trade = CycleRun.Simulate(path, Noon, new TradePlan(TradeSide.Short, FourHours), Costs, amount: 100).ToTrade();
        Assert.That(cycle, Is.Not.Null);
        Assert.That(cycle!.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(cycle.Plan, Is.EqualTo(new TradePlan(TradeSide.Short, FourHours)));
        Assert.That(cycle.Outcome, Is.EqualTo(trade), "the same entry, the same exit, the same outcome");
        Assert.That(cycle.Outcome!.EntryTime, Is.EqualTo(Noon));
        Assert.That(cycle.ExitTime, Is.EqualTo(FourPm));
        Assert.That(cycle.Outcome.Net, Is.Not.Zero);
        Assert.That(position.BusyUntil, Is.EqualTo(FourPm));
    }

    [Test]
    public void A_cycle_with_a_stop_ends_at_the_minute_of_the_stop()
    {
        MinutePath path = StoppedAtTheSeventhMinute();

        Cycle? cycle = new PairPosition().Enter(path, Order(Noon, EntrySide.Long, OneDay, stop: 0.02m), Costs);

        Assert.That(cycle!.ExitTime, Is.EqualTo(Paths.At(7)));
        Assert.That(cycle.Outcome!.ExitReason, Is.EqualTo(ExitReason.Stop));
        Assert.That(cycle.Outcome, Is.EqualTo(CycleRun.Simulate(path, Noon, new TradePlan(TradeSide.Long, OneDay, stop: 0.02), Costs, amount: 100).ToTrade()));
    }

    [Test]
    public void An_order_while_the_position_is_open_is_skipped()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var position = new PairPosition();
        position.Enter(path, Order(Noon, EntrySide.Long, OneDay), Costs);

        Cycle? second = position.Enter(path, Order(FourPm, EntrySide.Long, OneDay), Costs);

        Assert.That(second, Is.Null, "the order at 16:00 opens nothing");
        Assert.That(position.BusyUntil, Is.EqualTo(NoonTomorrow), "and does not change the cycle that runs to 12:00 of the next day");
    }

    [Test]
    public void A_stop_frees_the_pair()
    {
        MinutePath path = StoppedAtTheSeventhMinute();
        var position = new PairPosition();
        Cycle? first = position.Enter(path, Order(Noon, EntrySide.Long, OneDay, stop: 0.02m), Costs);

        Cycle? second = position.Enter(path, Order(FourPm, EntrySide.Long, OneDay, stop: 0.02m), Costs);

        Assert.That(first!.ExitTime, Is.EqualTo(Paths.At(7)));
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(second.Outcome!.EntryTime, Is.EqualTo(FourPm));
    }

    [Test]
    public void An_order_at_the_moment_of_the_close_opens_a_new_cycle()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var position = new PairPosition();
        Cycle? first = position.Enter(path, Order(Noon, EntrySide.Short, FourHours), Costs);

        Cycle? second = position.Enter(path, Order(FourPm, EntrySide.Short, FourHours), Costs);

        Assert.That(first!.ExitTime, Is.EqualTo(FourPm));
        Assert.That(first.Outcome!.ExitReason, Is.EqualTo(ExitReason.Time));
        Assert.That(second!.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(position.BusyUntil, Is.EqualTo(EightPm));
    }

    [Test]
    public void An_order_a_minute_before_the_close_is_skipped()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var position = new PairPosition();
        position.Enter(path, Order(Noon, EntrySide.Short, FourHours), Costs);

        Assert.That(position.Enter(path, Order(FourPm.AddMinutes(-1), EntrySide.Short, FourHours), Costs), Is.Null);
    }

    [Test]
    public void Another_pair_has_its_own_position()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var btc = new PairPosition();
        var eth = new PairPosition();
        btc.Enter(path, Order(Noon, EntrySide.Long, OneDay), Costs);

        Cycle? onEth = eth.Enter(path, Order(FourPm, EntrySide.Long, OneDay), Costs);

        Assert.That(onEth!.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(btc.BusyUntil, Is.EqualTo(NoonTomorrow));
        Assert.That(eth.BusyUntil, Is.EqualTo(FourPm + OneDay));
    }

    [Test]
    public void A_trade_left_out_for_a_hole_in_the_history_does_not_hold_the_position()
    {
        MinutePath path = new Paths().Flat(100, 5).Unknown().Flat(100, 3 * Day).Build();
        var position = new PairPosition();

        Cycle? first = position.Enter(path, Order(Noon, EntrySide.Long, FourHours), Costs);
        Cycle? second = position.Enter(path, Order(FourPm, EntrySide.Long, FourHours), Costs);

        Assert.That(first!.Status, Is.EqualTo(TradeStatus.Coverage));
        Assert.That(first.ExitTime, Is.Null);
        Assert.That(first.Outcome, Is.EqualTo(TradeOutcome.Excluded(TradeStatus.Coverage)));
        Assert.That(second!.Status, Is.EqualTo(TradeStatus.Ok), "the order at 16:00 is taken with the position closed");
    }

    [Test]
    public void A_trade_left_out_for_no_trades_or_for_funding_does_not_hold_the_position()
    {
        MinutePath quiet = new Paths().Quiet(4 * Hour).Flat(100, 3 * Day).Build();
        var noFunding = new MinutePath(
            Paths.Start,
            [.. Enumerable.Repeat(100.0, Day)],
            [.. Enumerable.Repeat(100.0, Day)],
            [.. Enumerable.Repeat(100.0, Day)],
            [.. Enumerable.Repeat(100.0, Day)],
            [.. Enumerable.Repeat(true, Day)],
            [],
            FourPm,
            NoonTomorrow);
        var onQuiet = new PairPosition();
        var onNoFunding = new PairPosition();

        Assert.That(onQuiet.Enter(quiet, Order(Noon, EntrySide.Long, FourHours), Costs)!.Status, Is.EqualTo(TradeStatus.NoTrade));
        Assert.That(onQuiet.Enter(quiet, Order(FourPm, EntrySide.Long, FourHours), Costs)!.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(onNoFunding.Enter(noFunding, Order(Noon, EntrySide.Long, FourHours), Costs)!.Status, Is.EqualTo(TradeStatus.Funding));
        Assert.That(onNoFunding.Enter(noFunding, Order(FourPm, EntrySide.Long, FourHours), Costs)!.Status, Is.EqualTo(TradeStatus.Ok));
    }

    [Test]
    public void An_order_whose_stop_is_not_computed_yet_is_refused_and_does_not_hold_the_position()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var position = new PairPosition();
        var notComputed = new StrategyStep(Noon, EntrySide.Long, OneDay, ExitDistance.Of(null), ExitDistance.None);

        Cycle? refused = position.Enter(path, notComputed, Costs);
        Cycle? next = position.Enter(path, Order(FourPm, EntrySide.Long, OneDay, stop: 0.02m), Costs);

        Assert.That(refused!.Status, Is.EqualTo(TradeStatus.Plan));
        Assert.That(refused.Plan, Is.Null);
        Assert.That(refused.ExitTime, Is.Null);
        Assert.That(refused.Outcome, Is.EqualTo(TradeOutcome.Excluded(TradeStatus.Plan)));
        Assert.That(position.BusyUntil, Is.EqualTo(FourPm + OneDay), "the next order was taken with the position closed");
        Assert.That(next!.Status, Is.EqualTo(TradeStatus.Ok));
    }

    [Test]
    public void An_order_whose_stop_is_outside_its_bounds_is_refused()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var position = new PairPosition();

        Cycle? cycle = position.Enter(path, Order(Noon, EntrySide.Long, OneDay, stop: 1.2m), Costs);

        Assert.That(cycle!.Status, Is.EqualTo(TradeStatus.Plan));
        Assert.That(position.BusyUntil, Is.Null, "the cycle is not open");
    }

    [Test]
    public void A_cycle_is_opened_outside_the_graph_at_a_candle_without_an_event()
    {
        // The candle of 16:00-20:00 gives no event of H0001: the rate is below the threshold.
        var executor = new StrategyExecutor(StrategyGraph.ReadStrategy(Graphs.H0001Strategy).Value!);
        var candle = new ClosedCandle(FourPm, EightPm, 100, 100, 100, 100, 1);
        StrategyStep step = executor.Step("BTCUSDT", candle, new FundingPayment(FourPm, 0.0001m));
        MinutePath path = new Paths().Flat(100, 9 * Hour).Flat(98, 3 * Day).Build();

        var cycle = Cycle.Open(path, step, EntrySide.Short, Costs);

        Assert.That(step.Entry, Is.Null);
        Assert.That(step.Moment, Is.EqualTo(EightPm));
        Assert.That(cycle.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(cycle.Outcome, Is.EqualTo(CycleRun.Simulate(path, EightPm, new TradePlan(TradeSide.Short, FourHours), Costs, amount: 100).ToTrade()));
        Assert.That(cycle.ExitTime, Is.EqualTo(EightPm + FourHours));
    }

    [Test]
    public void A_cycle_opened_outside_the_graph_does_not_see_and_does_not_change_the_cycle_of_an_order()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var position = new PairPosition();
        Cycle? ofOrder = position.Enter(path, Order(Noon, EntrySide.Long, OneDay), Costs);

        var outside = Cycle.Open(path, new StrategyStep(FourPm, Entry: null, OneDay, ExitDistance.None, ExitDistance.None), EntrySide.Long, Costs);

        Assert.That(outside.Status, Is.EqualTo(TradeStatus.Ok), "it is opened though the cycle of the order runs");
        Assert.That(outside.Outcome, Is.EqualTo(CycleRun.Simulate(path, FourPm, new TradePlan(TradeSide.Long, OneDay), Costs, amount: 100).ToTrade()));
        Assert.That(ofOrder!.ExitTime, Is.EqualTo(NoonTomorrow));
        Assert.That(position.BusyUntil, Is.EqualTo(NoonTomorrow), "the cycle of the order did not change");
        Assert.That(position.Enter(path, Order(EightPm, EntrySide.Long, OneDay), Costs), Is.Null, "and still holds the pair");
    }

    [Test]
    public void A_cycle_outside_the_graph_at_a_candle_without_a_stop_is_refused()
    {
        MinutePath path = new Paths().Flat(100, 3 * Day).Build();
        var step = new StrategyStep(Noon, Entry: null, OneDay, ExitDistance.Of(null), ExitDistance.None);

        var cycle = Cycle.Open(path, step, EntrySide.Long, Costs);

        Assert.That(cycle.Status, Is.EqualTo(TradeStatus.Plan));
        Assert.That(cycle.Opened, Is.False);
    }

    [Test]
    public void The_end_of_a_cycle_is_known_without_its_result()
    {
        MinutePath path = StoppedAtTheSeventhMinute();
        var blind = new PairPosition();
        var seeing = new PairPosition();
        StrategyStep order = Order(Noon, EntrySide.Long, OneDay, stop: 0.02m);

        Cycle? withoutResult = blind.Enter(path, order, Costs, withOutcome: false);
        Cycle? withResult = seeing.Enter(path, order, Costs);

        Assert.That(withoutResult!.Outcome, Is.Null);
        Assert.That((withoutResult.Status, withoutResult.Plan, withoutResult.ExitTime), Is.EqualTo((withResult!.Status, withResult.Plan, withResult.ExitTime)));
        Assert.That(blind.BusyUntil, Is.EqualTo(seeing.BusyUntil).And.EqualTo(Paths.At(7)));
        Assert.That(Cycle.Open(path, order with { After = null }, EntrySide.Long, Costs, withOutcome: false).Outcome, Is.Null);
    }

    [Test]
    public void A_step_without_an_order_cannot_enter()
    {
        MinutePath path = new Paths().Flat(100, Day).Build();
        var step = new StrategyStep(Noon, Entry: null, FourHours, ExitDistance.None, ExitDistance.None);

        Assert.That(() => new PairPosition().Enter(path, step, Costs), Throws.ArgumentException);
    }

    // A long from 100 with a stop of 2 % is stopped in the minute of 12:07, where the low is 97.
    private static MinutePath StoppedAtTheSeventhMinute()
    {
        return new Paths().Flat(100, 7).Minute(100, 100, 97, 97).Flat(100, 3 * Day).Build();
    }

    private static StrategyStep Order(DateTimeOffset moment, EntrySide side, TimeSpan after, decimal? stop = null)
    {
        return new StrategyStep(moment, side, after, stop is null ? ExitDistance.None : ExitDistance.Of(stop), ExitDistance.None);
    }
}
