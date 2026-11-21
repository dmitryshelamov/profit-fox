using ProfitFox.Domain.Trades;

namespace ProfitFox.Tests.Unit.Trades;

/// <summary>
///     Spec trade-plan-outcome: the plan, the entry, stop and target, the exit by the horizon, costs and trades left out.
/// </summary>
public sealed class TradeSimulatorTests
{
    private const double Slippage = 0.0005;
    private static readonly TradeCosts Costs = TradeCosts.Default;
    private static readonly TradeCosts NoSlippage = new TradeCosts(Slippage: 0);

    [Test]
    public void A_stop_outside_its_bounds_is_refused()
    {
        Assert.That(() => new TradePlan(TradeSide.Long, TimeSpan.FromHours(4), stop: 1.2),
            Throws.InstanceOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("stop"));
    }

    [Test]
    public void A_horizon_shorter_than_a_minute_is_refused()
    {
        Assert.That(() => new TradePlan(TradeSide.Long, TimeSpan.FromSeconds(30)), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void The_entry_skips_a_minute_without_trades()
    {
        MinutePath path = new Paths().Quiet().Minute(100, 100, 100, 100).Flat(100, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(5)), Costs);

        Assert.That(outcome.EntryTime, Is.EqualTo(Paths.At(1)));
        Assert.That(outcome.EntryPrice, Is.EqualTo(100.05).Within(1e-12));
    }

    [Test]
    public void A_gap_through_the_stop_fills_at_the_open()
    {
        double entryOpen = 100 / (1 + Slippage);
        MinutePath path = new Paths().Minute(entryOpen, entryOpen, entryOpen, entryOpen).Minute(98, 98, 97, 97.5).Flat(97.5, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10), stop: 0.01), Costs);

        Assert.That(outcome.ExitReason, Is.EqualTo(ExitReason.Stop));
        Assert.That(outcome.ExitPrice, Is.EqualTo(98 * (1 - Slippage)).Within(1e-12));
        Assert.That(outcome.ExitTime, Is.EqualTo(Paths.At(1)));
    }

    [Test]
    public void Stop_and_target_in_one_minute_count_as_the_stop()
    {
        MinutePath path = new Paths().Flat(100, 1).Minute(100, 103, 97, 100).Flat(100, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(
            path,
            Paths.Start,
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10), stop: 0.01, target: 0.01),
            NoSlippage);

        Assert.That(outcome.ExitReason, Is.EqualTo(ExitReason.Stop));
        Assert.That(outcome.Ambiguous, Is.True);
        Assert.That(outcome.ExitPrice, Is.EqualTo(99).Within(1e-12));
    }

    [Test]
    public void The_target_fills_at_its_price_without_slippage_even_on_a_gap()
    {
        MinutePath path = new Paths().Flat(100, 1).Minute(103, 104, 102.5, 103.5).Flat(103.5, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10), target: 0.01), Costs);

        Assert.That(outcome.ExitReason, Is.EqualTo(ExitReason.Target));
        Assert.That(outcome.ExitPrice, Is.EqualTo(100 * (1 + Slippage) * 1.01).Within(1e-12));
    }

    [Test]
    public void The_horizon_counts_from_the_event_and_closes_at_the_last_minute_with_trades()
    {
        MinutePath path = new Paths().Quiet().Flat(100, 237).Minute(101, 101, 101, 101.5).Quiet().Flat(90, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Short, TimeSpan.FromHours(4)), Costs);

        Assert.That(outcome.EntryTime, Is.EqualTo(Paths.At(1)));
        Assert.That(outcome.ExitTime, Is.EqualTo(Paths.At(240)));
        Assert.That(outcome.ExitReason, Is.EqualTo(ExitReason.Time));
        Assert.That(outcome.ExitPrice, Is.EqualTo(101.5 * (1 + Slippage)).Within(1e-12));
    }

    [Test]
    public void A_short_through_a_positive_payment_receives_it()
    {
        MinutePath path = new Paths().Flat(100, 30).Payment(10, 0.001).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Short, TimeSpan.FromMinutes(20)), NoSlippage);

        Assert.That(outcome.Funding, Is.EqualTo(0.001).Within(1e-15));
        Assert.That(outcome.Net, Is.EqualTo(-2 * 0.00055 + 0.001).Within(1e-15));
    }

    [Test]
    public void A_payment_at_the_entry_minute_is_not_paid_and_at_the_exit_minute_is()
    {
        MinutePath path = new Paths().Flat(100, 30).Payment(0, 0.01).Payment(20, 0.001).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(20)), NoSlippage);

        Assert.That(outcome.Funding, Is.EqualTo(-0.001).Within(1e-15));
    }

    [Test]
    public void Fees_are_taken_on_both_sides()
    {
        MinutePath path = new Paths().Flat(100, 5).Flat(110, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)), NoSlippage);

        Assert.That(outcome.Gross, Is.EqualTo(0.1).Within(1e-12));
        Assert.That(outcome.Fees, Is.EqualTo(0.00055 * (1 + 1.1)).Within(1e-12));
    }

    [Test]
    public void Without_a_stop_there_is_no_result_in_R()
    {
        MinutePath path = new Paths().Flat(100, 30).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)), Costs);

        Assert.That(outcome.R, Is.Null);
        Assert.That(outcome.Net, Is.Not.Null);
    }

    [Test]
    public void A_minute_outside_coverage_leaves_the_trade_out()
    {
        MinutePath path = new Paths().Flat(100, 5).Unknown().Flat(100, 10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)), Costs);

        Assert.That(outcome, Is.EqualTo(TradeOutcome.Excluded(TradeStatus.Coverage)));
    }

    [Test]
    public void A_horizon_without_trades_leaves_the_trade_out()
    {
        MinutePath path = new Paths().Quiet(10).Build();

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)), Costs);

        Assert.That(outcome.Status, Is.EqualTo(TradeStatus.NoTrade));
    }

    [Test]
    public void A_horizon_past_the_known_funding_leaves_the_trade_out_even_if_it_exits_earlier()
    {
        // Funding is known up to minute 15; the stop at minute 1 ends the trade long before, the 20-minute horizon does not.
        MinutePath path = new MinutePath(
            Paths.Start,
            [.. Enumerable.Repeat(100.0, 22)],
            [.. Enumerable.Repeat(100.0, 22)],
            [.. Enumerable.Repeat(100.0, 1).Concat([90.0]).Concat(Enumerable.Repeat(100.0, 20))],
            [.. Enumerable.Repeat(100.0, 22)],
            [.. Enumerable.Repeat(true, 22)],
            [],
            Paths.Start,
            Paths.At(15));

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(20), stop: 0.05), Costs);

        Assert.That(outcome.Status, Is.EqualTo(TradeStatus.Funding));
    }

    [Test]
    public void A_pair_without_funding_pays_none()
    {
        MinutePath path = new Paths().Flat(100, 30).Payment(10, 0.01).Build(withFunding: false);

        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, new TradePlan(TradeSide.Short, TimeSpan.FromMinutes(20)), Costs);

        Assert.That(outcome.Funding, Is.EqualTo(0));
    }
}
