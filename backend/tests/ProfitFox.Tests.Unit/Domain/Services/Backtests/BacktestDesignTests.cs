using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Hypotheses.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.1 (design.md, decision 4): the time of an order is that of its step, not of a
///     plan, and the seed of the draw is an input of the calculation.
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
    public void A_random_entry_is_a_close_whose_candle_has_a_time_that_ends_by_the_end()
    {
        BacktestDesign design = Design(end: Monday.AddDays(2));
        // The candles of Monday give 4h, those of Tuesday 2d: none of Tuesday fits before Wednesday 00:00.
        Func<DateTimeOffset, TimeSpan?> after = moment => moment < Monday.AddDays(1) ? FourHours : TimeSpan.FromDays(2);

        IReadOnlyList<DateTimeOffset> candidates = new RandomEntries("BTCUSDT", [Monday.AddHours(12)], design, after).Candidates(Week);

        Assert.That(candidates, Has.All.LessThan(Monday.AddDays(1)));
        Assert.That(candidates, Has.Count.EqualTo(5), "00:00 to 20:00 of Monday but the order at 12:00");
        Assert.That(candidates, Has.None.EqualTo(Monday.AddHours(12)));
    }

    [Test]
    public void The_draw_follows_the_seed()
    {
        BacktestDesign design = Design(end: Monday.AddDays(7));
        IReadOnlyList<DateTimeOffset> candidates = new RandomEntries("BTCUSDT", [], design, _ => FourHours).Candidates(Week);

        DateTimeOffset[] first = new RandomEntries("BTCUSDT", [], design, _ => FourHours).Draw(candidates);
        DateTimeOffset[] again = new RandomEntries("BTCUSDT", [], design, _ => FourHours).Draw(candidates);
        DateTimeOffset[] other = new RandomEntries("BTCUSDT", [], design with { Seed = "another" }, _ => FourHours).Draw(candidates);

        Assert.That(again, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
        Assert.That(first, Has.Length.EqualTo(20));
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
        return new BacktestDesign(Monday, end, FourHours, "seed", TradeCosts.Default);
    }

    // Steps with the given times, some of them orders; the rest of the week has no step.
    private static PairSteps Steps(params (DateTimeOffset Moment, TimeSpan After, EntrySide? Side)[] steps)
    {
        var before = new StrategyStep(DateTimeOffset.UnixEpoch, Entry: null, FourHours, ExitDistance.None, ExitDistance.None);
        return new PairSteps([.. steps.Select(step => new StrategyStep(step.Moment, step.Side, step.After, ExitDistance.None, ExitDistance.None))], before);
    }
}
