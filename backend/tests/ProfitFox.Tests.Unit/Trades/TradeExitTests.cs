using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Trades;

/// <summary>
///     Change add-strategy-graph, design.md, decision 6 (task 3.2): the moment a trade ends, known without computing its
///     result. On every case of <see cref="TradeSimulatorTests" /> the moment and the status are those of
///     <see cref="TradeSimulator.Simulate" />.
/// </summary>
public sealed class TradeExitTests
{
    private const double Slippage = 0.0005;
    private static readonly TradeCosts Costs = TradeCosts.Default;
    private static readonly TradeCosts NoSlippage = new TradeCosts(Slippage: 0);

    // The paths, plans and costs of TradeSimulatorTests, in its order.
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return Case(
            "The entry skips a minute without trades",
            new Paths().Quiet().Minute(100, 100, 100, 100).Flat(100, 10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(5)),
            Costs);

        double entryOpen = 100 / (1 + Slippage);
        yield return Case(
            "A gap through the stop",
            new Paths().Minute(entryOpen, entryOpen, entryOpen, entryOpen).Minute(98, 98, 97, 97.5).Flat(97.5, 10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10), stop: 0.01),
            Costs);
        yield return Case(
            "Stop and target in one minute",
            new Paths().Flat(100, 1).Minute(100, 103, 97, 100).Flat(100, 10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10), stop: 0.01, target: 0.01),
            NoSlippage);
        yield return Case(
            "The target on a gap",
            new Paths().Flat(100, 1).Minute(103, 104, 102.5, 103.5).Flat(103.5, 10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10), target: 0.01),
            Costs);
        yield return Case(
            "The horizon counts from the event",
            new Paths().Quiet().Flat(100, 237).Minute(101, 101, 101, 101.5).Quiet().Flat(90, 10).Build(),
            new TradePlan(TradeSide.Short, TimeSpan.FromHours(4)),
            Costs);
        yield return Case(
            "A short through a positive payment",
            new Paths().Flat(100, 30).Payment(10, 0.001).Build(),
            new TradePlan(TradeSide.Short, TimeSpan.FromMinutes(20)),
            NoSlippage);
        yield return Case(
            "Payments at the entry and the exit minute",
            new Paths().Flat(100, 30).Payment(0, 0.01).Payment(20, 0.001).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(20)),
            NoSlippage);
        yield return Case(
            "Fees on both sides",
            new Paths().Flat(100, 5).Flat(110, 10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)),
            NoSlippage);
        yield return Case(
            "Without a stop",
            new Paths().Flat(100, 30).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)),
            Costs);
        yield return Case(
            "A minute outside coverage",
            new Paths().Flat(100, 5).Unknown().Flat(100, 10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)),
            Costs);
        yield return Case(
            "A horizon without trades",
            new Paths().Quiet(10).Build(),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(10)),
            Costs);
        yield return Case(
            "A horizon past the known funding",
            new MinutePath(
                Paths.Start,
                [.. Enumerable.Repeat(100.0, 22)],
                [.. Enumerable.Repeat(100.0, 22)],
                [.. Enumerable.Repeat(100.0, 1).Concat([90.0]).Concat(Enumerable.Repeat(100.0, 20))],
                [.. Enumerable.Repeat(100.0, 22)],
                [.. Enumerable.Repeat(true, 22)],
                [],
                Paths.Start,
                Paths.At(15)),
            new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(20), stop: 0.05),
            Costs);
        yield return Case(
            "A pair without funding",
            new Paths().Flat(100, 30).Payment(10, 0.01).Build(withFunding: false),
            new TradePlan(TradeSide.Short, TimeSpan.FromMinutes(20)),
            Costs);
    }

    [TestCaseSource(nameof(Cases))]
    public void The_exit_is_at_the_moment_and_with_the_status_of_the_simulated_trade(MinutePath path, TradePlan plan, TradeCosts costs)
    {
        TradeOutcome outcome = TradeSimulator.Simulate(path, Paths.Start, plan, costs);

        TradeExit exit = TradeSimulator.Exit(path, Paths.Start, plan, costs);

        Assert.That(exit, Is.EqualTo(new TradeExit(outcome.Status, outcome.ExitTime)));
        Assert.That(exit.Time is null, Is.EqualTo(outcome.Status != TradeStatus.Ok), "a trade left out has no exit");
    }

    [Test]
    public void The_cases_end_by_the_stop_by_the_target_by_the_horizon_and_are_left_out()
    {
        (ExitReason Reason, TradeStatus Status)[] ends =
        [
            .. Cases().Select(data => TradeSimulator.Simulate((MinutePath)data.Arguments[0]!, Paths.Start, (TradePlan)data.Arguments[1]!, (TradeCosts)data.Arguments[2]!))
                .Select(outcome => (outcome.ExitReason, outcome.Status))
        ];

        Assert.That(ends.Select(end => end.Reason).Distinct(), Is.EquivalentTo(Enum.GetValues<ExitReason>()));
        Assert.That(ends.Select(end => end.Status).Distinct(), Is.EquivalentTo(new[] { TradeStatus.Ok, TradeStatus.Coverage, TradeStatus.NoTrade, TradeStatus.Funding }));
    }

    [Test]
    public void A_stop_ends_the_trade_at_its_minute_and_the_horizon_at_its_end()
    {
        MinutePath path = new Paths().Flat(100, 7).Minute(100, 100, 97, 97).Flat(97, 20).Build();

        TradeExit stopped = TradeSimulator.Exit(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(20), stop: 0.02), Costs);
        TradeExit timed = TradeSimulator.Exit(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromMinutes(20)), Costs);

        Assert.That(stopped, Is.EqualTo(new TradeExit(TradeStatus.Ok, Paths.At(7))));
        Assert.That(timed, Is.EqualTo(new TradeExit(TradeStatus.Ok, Paths.At(20))));
    }

    private static TestCaseData Case(string name, MinutePath path, TradePlan plan, TradeCosts costs)
    {
        return new TestCaseData(path, plan, costs).SetArgDisplayNames(name);
    }
}
