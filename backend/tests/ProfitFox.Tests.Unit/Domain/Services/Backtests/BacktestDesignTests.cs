using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.1 (design.md, decision 4): the time of an order is that of its step, not of a
///     plan; a random entry takes the time of the step of its own pair (change fix-backtest-random-control).
/// </summary>
public sealed class BacktestDesignTests
{
    private static readonly DateTimeOffset Monday = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly long Week = Blocks.Of(Monday, TimeBlock.Week);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void An_order_is_in_history_when_its_own_time_ends_by_the_end()
    {
        BacktestDesign design = Design(end: Monday.AddHours(20));
        // At 12:00 a time of 4h ends at 16:00; at 14:00 a time of 1d ends on Tuesday, after the end.
        PairSteps steps = Steps((Monday.AddHours(12), FourHours, EntrySide.Short), (Monday.AddHours(14), TimeSpan.FromDays(1), EntrySide.Short));

        var evaluation = new PairEvaluation("BTCUSDT", steps, design);

        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week }));
        Assert.That(evaluation.PastEnd, Is.EqualTo(1));
        Assert.That(design.Fits(Monday.AddHours(16), FourHours), Is.True, "a trade that ends at the end is in history");
        Assert.That(design.Fits(Monday.AddHours(17), FourHours), Is.False);
        Assert.That(design.Fits(Monday.AddHours(-4), FourHours), Is.False, "nor before the start");
    }

    [Test]
    public void A_random_entry_is_taken_when_the_time_of_the_step_of_its_pair_ends_by_the_end()
    {
        BacktestDesign design = Design(end: Monday.AddDays(2));
        // The steps of ETHUSDT give 4h on Monday and 2d from Tuesday: an entry on Tuesday does not end before Wednesday 00:00.
        PairSteps steps = Steps((Monday, FourHours, null), (Monday.AddDays(1), TimeSpan.FromDays(2), null));
        var orders = new SetOrders(
            new Dictionary<string, IReadOnlyList<PairOrder>> { ["SOLUSDT"] = [new PairOrder(Monday.AddHours(12), EntrySide.Short), new PairOrder(Monday.AddDays(1).AddHours(4), EntrySide.Short)] },
            TimeBlock.Week);
        var groups = new ControlGroups(new Dictionary<string, DateTimeOffset> { ["ETHUSDT"] = Monday }, TimeBlock.Week, size: 20);

        var evaluation = new PairEvaluation("ETHUSDT", steps, design, new SetControl(orders, groups));

        (DateTimeOffset from, DateTimeOffset to) = evaluation.Span(Week);
        int length = (int)(to - from).TotalMinutes;
        double[] prices = [.. Enumerable.Repeat(100.0, length)];
        var path = new MinutePath(from, prices, prices, prices, prices, [.. Enumerable.Repeat(true, length)], [], from, to.AddDays(1));

        Assert.That(evaluation.Blocks, Is.EqualTo(new[] { Week }));
        Assert.That(to, Is.EqualTo(Monday.AddDays(2)), "never past the end");
        Assert.That(evaluation.Evaluate(Week, path).Randoms.Select(random => random.Entry), Is.EqualTo(new[] { Monday.AddHours(12) }));
    }

    [Test]
    public void The_minutes_of_a_week_reach_past_it_by_the_longest_time_of_its_steps()
    {
        BacktestDesign design = Design(end: Monday.AddDays(21));
        PairSteps steps = Steps((Monday.AddHours(12), FourHours, EntrySide.Short), (Monday.AddDays(3), TimeSpan.FromDays(2), null));

        (DateTimeOffset from, DateTimeOffset to) = new PairEvaluation("BTCUSDT", steps, design).Span(Week);

        Assert.That((from, to), Is.EqualTo((Monday, Monday.AddDays(9))), "a random entry on Thursday may last two days");
        Assert.That(Design(end: Monday.AddDays(8)).Span(Week, TimeSpan.FromDays(2)).To, Is.EqualTo(Monday.AddDays(8)), "never past the end");
    }

    private static BacktestDesign Design(DateTimeOffset end)
    {
        return new BacktestDesign(Monday, end, FourHours, TradeCosts.Default);
    }

    // Steps with the given times, some of them orders; the rest of the week has no step.
    private static PairSteps Steps(params (DateTimeOffset Moment, TimeSpan After, EntrySide? Side)[] steps)
    {
        var before = new StrategyStep(DateTimeOffset.UnixEpoch, Entry: null, FourHours, ExitDistance.None, ExitDistance.None);
        return new PairSteps([.. steps.Select(step => new StrategyStep(step.Moment, step.Side, step.After, ExitDistance.None, ExitDistance.None))], before);
    }
}