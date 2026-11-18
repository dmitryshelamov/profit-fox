using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Simulation.Orders;
using ProfitFox.Engine.Simulation.Plans;

namespace ProfitFox.Tests.Unit.Engine.Simulation.Plans;

[TestFixture]
public class TradePlanOutcomesTests
{
    private static readonly Costs Costs = new Costs(takerFee: 0.00055m, slippage: 0.0005m);
    private static readonly TradePlan LongThree = new TradePlan(PositionSide.Long, TimeSpan.FromMinutes(3));
    private static readonly TradePlan ShortThree = new TradePlan(PositionSide.Short, TimeSpan.FromMinutes(3));

    [TestCase(0)]
    [TestCase(30)]
    [TestCase(90)]
    public void A_horizon_that_is_not_a_whole_number_of_minutes_is_rejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TradePlan(PositionSide.Short, TimeSpan.FromSeconds(seconds)));
    }

    [Test]
    public void A_trade_enters_at_its_moment_and_exits_at_the_end_of_the_horizon_by_market()
    {
        MinutePath path = Paths.Path(minutes: 4, [Paths.Flat(0, 100m), Paths.Flat(1, 101m), Paths.Flat(2, 103m), Paths.Flat(3, 102m)]);

        TradeOutcome outcome = TradePlanOutcomes.Compute(path, LongThree, Costs, [Paths.Ten]).Single();

        decimal ratio = 101.949m / 100.05m;
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(TradeStatus.Ok));
            Assert.That(outcome.EntryTime, Is.EqualTo(Paths.Minute(0)));
            Assert.That(outcome.ExitTime, Is.EqualTo(Paths.Minute(3)));
            Assert.That(outcome.EntryPrice, Is.EqualTo(100.05m));
            Assert.That(outcome.ExitPrice, Is.EqualTo(101.949m));
            Assert.That(outcome.Gross, Is.EqualTo(0.02m));
            Assert.That(outcome.Slippage!.Value, Is.EqualTo(0.02m - (ratio - 1)).Within(1e-25m));
            Assert.That(outcome.Fees!.Value, Is.EqualTo(0.00055m * (1 + ratio)).Within(1e-25m));
            Assert.That(outcome.Funding, Is.EqualTo(0m));
            Assert.That(outcome.Net!.Value, Is.EqualTo(ratio - 1 - 0.00055m * (1 + ratio)).Within(1e-25m));
        });
    }

    [Test]
    public void A_short_held_through_a_payment_receives_its_funding()
    {
        MinutePath path = Paths.Path(
            minutes: 4,
            [Paths.Flat(0, 100m), Paths.Flat(1, 101m), Paths.Flat(2, 100m), Paths.Flat(3, 100m)],
            [Paths.Payment(2, 0.0001m)]);

        TradeOutcome outcome = TradePlanOutcomes.Compute(path, ShortThree, Costs, [Paths.Ten]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.EntryPrice, Is.EqualTo(99.95m));
            Assert.That(outcome.Funding!.Value, Is.EqualTo(0.0001m * 101m / 99.95m).Within(1e-25m));
        });
    }

    [Test]
    public void An_uncovered_minute_inside_the_horizon_excludes_the_trade_for_coverage()
    {
        MinutePath path = Paths.Path(
            minutes: 4,
            [Paths.Flat(0, 100m), Paths.Flat(2, 100m), Paths.Flat(3, 100m)],
            coverage: [new TimeRange(Paths.Minute(0), Paths.Minute(1)), new TimeRange(Paths.Minute(2), Paths.Minute(4))]);

        Assert.That(TradePlanOutcomes.Compute(path, LongThree, Costs, [Paths.Ten]).Single().Status, Is.EqualTo(TradeStatus.ExcludedCoverage));
    }

    [Test]
    public void An_exit_that_cannot_fill_excludes_the_trade_for_coverage()
    {
        MinutePath path = Paths.Path(minutes: 3, [Paths.Flat(0, 100m), Paths.Flat(1, 100m), Paths.Flat(2, 100m)]);

        Assert.That(TradePlanOutcomes.Compute(path, LongThree, Costs, [Paths.Ten]).Single().Status, Is.EqualTo(TradeStatus.ExcludedCoverage));
    }

    [Test]
    public void A_pair_without_trades_over_the_horizon_excludes_the_trade_for_no_trade()
    {
        MinutePath path = Paths.Path(minutes: 4, [Paths.Flat(0, 100m, volume: 0m), Paths.Flat(3, 100m)]);

        Assert.That(TradePlanOutcomes.Compute(path, LongThree, Costs, [Paths.Ten]).Single().Status, Is.EqualTo(TradeStatus.ExcludedNoTrade));
    }

    [Test]
    public void Payments_unknown_up_to_the_exit_exclude_the_trade_for_funding()
    {
        MinutePath path = Paths.Path(
            minutes: 4,
            [Paths.Flat(0, 100m), Paths.Flat(3, 100m)],
            fundingCoverage: new TimeRange(Paths.Minute(0), Paths.Minute(3)));

        TradeOutcome outcome = TradePlanOutcomes.Compute(path, LongThree, Costs, [Paths.Ten]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(TradeStatus.ExcludedFunding));
            Assert.That(outcome.Net, Is.Null);
        });
    }

    [Test]
    public void Coverage_is_checked_before_trades_and_trades_before_funding()
    {
        MinutePath path = Paths.Path(
            minutes: 4,
            [],
            coverage: [new TimeRange(Paths.Minute(0), Paths.Minute(1)), new TimeRange(Paths.Minute(2), Paths.Minute(4))],
            fundingCoverage: new TimeRange(Paths.Minute(0), Paths.Minute(1)));

        Assert.That(TradePlanOutcomes.Compute(path, LongThree, Costs, [Paths.Ten]).Single().Status, Is.EqualTo(TradeStatus.ExcludedCoverage));
    }

    [Test]
    public void Many_entries_in_one_call_give_the_outcomes_of_one_by_one()
    {
        MinutePath path = Paths.Path(minutes: 120, Enumerable.Range(0, 120).Select(minute => Paths.Flat(minute, 100m + minute % 7)));
        var moments = Enumerable.Range(0, 100).Select(minute => Paths.Minute(minute)).ToList();

        IReadOnlyList<TradeOutcome> together = TradePlanOutcomes.Compute(path, ShortThree, Costs, moments);
        var oneByOne = moments.Select(moment => TradePlanOutcomes.Compute(path, ShortThree, Costs, [moment]).Single()).ToList();

        Assert.That(together, Is.EqualTo(oneByOne));
    }

    [Test]
    public void Minutes_before_the_entry_and_after_the_exit_fill_change_nothing()
    {
        MinutePath first = Paths.Path(
            minutes: 6,
            [Paths.Flat(0, 50m), Paths.Flat(1, 100m), Paths.Flat(2, 101m), Paths.Flat(3, 102m), Paths.Flat(4, 103m), Paths.Flat(5, 70m)],
            [Paths.Payment(0, 0.5m), Paths.Payment(3, 0.0001m), Paths.Payment(5, 0.5m)]);
        MinutePath second = Paths.Path(
            minutes: 6,
            [Paths.Flat(0, 150m), Paths.Flat(1, 100m), Paths.Flat(2, 101m), Paths.Flat(3, 102m), Paths.Flat(4, 103m), Paths.Flat(5, 130m)],
            [Paths.Payment(0, -0.5m), Paths.Payment(3, 0.0001m), Paths.Payment(5, -0.5m)]);

        TradeOutcome onFirst = TradePlanOutcomes.Compute(first, ShortThree, Costs, [Paths.Minute(1)]).Single();
        TradeOutcome onSecond = TradePlanOutcomes.Compute(second, ShortThree, Costs, [Paths.Minute(1)]).Single();

        Assert.That(onFirst.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That(onSecond, Is.EqualTo(onFirst));
    }
}