using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>
///     Decision 8 on a reference set of «Цель 2R» on 15 minutes without fees, entered at 100 with the stop at 98, three keys:
///     <list type="bullet">
///         <item>A, 40 days ago: +0.5 R at 10 minutes, +1 R at 20, +1.5 R at 30, +2 R at 40, +2.5 R at 500, the stop at 600;</item>
///         <item>B, 10 days ago: +0.5 R at 10, the stop at 50;</item>
///         <item>C, 5 days ago: no level, the stop at 1000, every close of the grid 100.5 (+0.25 R).</item>
///     </list>
///     The rule now is 24 · 2 R, the candidate 48 · 2.5 R.
/// </summary>
public class ExitRulePreviewsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly FeeRates NoFees = new FeeRates(0m, 0m);
    private static readonly IdeaExitRule Current = new IdeaExitRules([]).Of(IdeaType.TwoR, Interval.FifteenMinutes);
    private static readonly IdeaExitRule Candidate = Current with { LifeCandles = 48, TargetR = 2.5m };

    // Scenario "Кандидат лучше на 90 днях": A takes +2 R now and +2.5 R with the candidate, B stops, C expires at +0.25 R by either.
    [Test]
    public void Preview_compares_the_rule_now_and_the_candidate_over_90_and_30_days()
    {
        ExitRulePreview preview = Compute();

        preview.Last90.ShouldBe(new ExitRuleWindow(
            BucketStatisticsCalculator.Compute(1, 1, 1, 1.25m, 5.0625m, 3),
            BucketStatisticsCalculator.Compute(1, 1, 1, 1.75m, 7.3125m, 3)));
        preview.Last30.ShouldBe(new ExitRuleWindow(
            BucketStatisticsCalculator.Compute(0, 1, 1, -0.75m, 1.0625m, 2),
            BucketStatisticsCalculator.Compute(0, 1, 1, -0.75m, 1.0625m, 2)));
    }

    // Scenario "Ряд по срокам": every lifetime of the grid at the target of the candidate; at one candle all three expire at +0.25 R, at 48
    // candles the candidate itself.
    [Test]
    public void Preview_has_the_expectancy_at_every_lifetime_of_the_grid()
    {
        ExitRulePreview preview = Compute();

        preview.Lives.Select(life => life.Candles).ShouldBe(ExitRuleGrid.Of(Interval.FifteenMinutes));
        preview.Lives[0].ShouldBe(new ExitRuleLife(1, 0.25m, 0.25m));
        preview.Lives.Single(life => life.Candles == 48).ShouldBe(new ExitRuleLife(48, 1.75m / 3, 1.75m / 3));
    }

    // Scenario "Кривая ниже безубытка": within 48 candles A and B reach +0.5 R, A alone the levels up to +2.5 R; without fees a target of x R
    // breaks even at 1 / (1 + x).
    [Test]
    public void Preview_has_the_curve_of_the_entries_that_reached_each_level_beside_its_break_even()
    {
        ExitRulePreview preview = Compute();

        preview.Mfe.Select(point => point.R).ShouldBe(SignalPath.Ladder);
        preview.Mfe.Select(point => point.Reached).ShouldBe([2m / 3, 1m / 3, 1m / 3, 1m / 3, 1m / 3, 0m, 0m, 0m, 0m, 0m]);
        preview.Mfe.Single(point => point.R == 2.5m).BreakEven.ShouldBe(1m / 3.5m);
    }

    // The fees of the category of each entry: a target of 0.5 R on a risk of 0.02 does not pay a taker of 1 % in and out.
    [Test]
    public void Break_even_of_a_target_that_does_not_pay_the_fees_is_all_of_the_entries()
    {
        ExitRulePreview preview = ExitRulePreviews.Compute(Signals(), Current, Candidate, _ => new FeeRates(1m, 1m), Now);

        preview.Mfe[0].BreakEven.ShouldBe(1m);
    }

    // No entry at all: neither window has statistics, and the curve is empty.
    [Test]
    public void Preview_without_entries_has_no_statistics()
    {
        ExitRulePreview preview = ExitRulePreviews.Compute([], Current, Candidate, _ => NoFees, Now);

        (preview.Last90, preview.Last30).ShouldBe((new ExitRuleWindow(null, null), new ExitRuleWindow(null, null)));
        preview.Lives[0].ShouldBe(new ExitRuleLife(1, null, null));
        preview.Mfe.ShouldBeEmpty();
    }

    private static ExitRulePreview Compute()
    {
        return ExitRulePreviews.Compute(Signals(), Current, Candidate, _ => NoFees, Now);
    }

    private static IReadOnlyList<IdeaSignal> Signals()
    {
        return
        [
            Signal(IdeaSetup.Drop, -40, ladder: [10, 20, 30, 40, 500], stopAfter: 600),
            Signal(IdeaSetup.EarlyImpulse, -10, ladder: [10], stopAfter: 50),
            Signal(IdeaSetup.Impulse, -5, ladder: [], stopAfter: 1000)
        ];
    }

    private static IdeaSignal Signal(IdeaSetup setup, int days, int[] ladder, int stopAfter)
    {
        IdeaSignal signal = IdeaSignalGenerator.Generate(Now.AddDays(days), IdeaStatus.Expired, setup: setup);
        int?[] touches = new int?[10];

        for (int index = 0; index < ladder.Length; index++)
        {
            touches[index] = ladder[index];
        }

        return signal with
        {
            Path = signal.Path with { LadderAfter = touches, StopAfter = stopAfter, Closes = [.. ExitRuleGrid.Of(Interval.FifteenMinutes).Select(_ => 100.5m)] }
        };
    }
}