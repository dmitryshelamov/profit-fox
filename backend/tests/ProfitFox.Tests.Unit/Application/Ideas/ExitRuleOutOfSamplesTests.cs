using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>
///     Requirements "Подбор и проверка" and "Лучшее правило сетки" of check-idea-exit-rules-out-of-sample on a reference set of «Цель 2R» on 15
///     minutes without fees, every signal of its own pair and hour, a long entered at 100 with the stop at 98:
///     <list type="bullet">
///         <item>
///             the selection, 40 signals 36 h apart from 90 days ago, 10 in each block of 15 days: the even ones stop at 1000 minutes with no
///             level, the odd ones touch every level up to +3 R at 1400 minutes and stop at 3000; every close of the grid at 100 (0 R);
///         </item>
///         <item>
///             the check, 40 signals 18 h apart from 30 days ago: no level; 30 stop at 1000 minutes, the last 10 never stop and close at 101
///             (+0.5 R).
///         </item>
///     </list>
///     On the selection 96 · 3 R and every longer lifetime at 3 R take +3 R on the odd half and −1 R on the even: +1 R, the best, the shortest
///     first. The candidate 24 · 2 R expires everything at its close.
/// </summary>
public class ExitRuleOutOfSamplesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly IdeaExitRule Candidate = new IdeaExitRules([]).Of(IdeaType.TwoR, Interval.FifteenMinutes);

    // Scenario "Лучшее на подборе слабее на проверке".
    [Test]
    public void Best_rule_of_the_selection_is_checked_on_the_last_30_days()
    {
        IReadOnlyList<IdeaSignal> signals = Signals();

        ExitRuleOutOfSample check = ExitRuleOutOfSamples.Compute(signals, Candidate, _ => new FeeRates(0m, 0m), Now);

        (check.SelectionFrom, check.CheckFrom, check.To, check.Rules).ShouldBe((Now.AddDays(-90), Now.AddDays(-30), Now, 171));
        check.Best!.Rule.ShouldBe(Candidate with { LifeCandles = 96, TargetR = 3m });
        check.Best.Parts.ShouldBe(new ExitRuleParts(
            Statistics(Selection().Select((firedAt, index) => (firedAt, index % 2 == 0 ? IdeaStatus.Stop : IdeaStatus.Target, index % 2 == 0 ? -1m : 3m))),
            Statistics(Check().Select((firedAt, index) => (firedAt, index < 30 ? IdeaStatus.Stop : IdeaStatus.Expired, index < 30 ? -1m : 0.5m)))));
        check.Best.Parts.Selection!.Expectancy.ShouldBe(1m);
        check.Best.Parts.Check!.Expectancy.ShouldBe(-0.625m);
        check.Candidate.ShouldBe(new ExitRuleParts(
            Statistics(Selection().Select(firedAt => (firedAt, IdeaStatus.Expired, 0m))),
            Statistics(Check().Select((firedAt, index) => (firedAt, IdeaStatus.Expired, index < 30 ? 0m : 0.5m)))));
        check.Overfitting.ShouldBe(OverfittingProbability.Of(ExitRuleGridPass.Sums(
            [.. signals.Select(signal => (new FeeRates(0m, 0m), (IReadOnlyList<SignalPath>)[signal.Path]))], ExitRuleGridPass.Rules(Candidate), Now.AddDays(-90))));
        check.Overfitting.ShouldNotBeNull();
    }

    // Scenario "Мало входов на подборе": 10 signals of the selection and none of the check.
    [Test]
    public void Check_without_30_entries_has_no_best_rule_and_no_overfitting()
    {
        IdeaSignal[] signals = [.. Signals().Take(10)];

        ExitRuleOutOfSample check = ExitRuleOutOfSamples.Compute(signals, Candidate, _ => new FeeRates(0m, 0m), Now);

        check.Best.ShouldBeNull();
        check.Overfitting.ShouldBeNull();
        check.Candidate.Check.ShouldBeNull();
        check.Candidate.Selection!.ExpectancyLow.ShouldBeNull();
    }

    private static BucketStatistics Statistics(IEnumerable<(DateTimeOffset FiredAt, IdeaStatus Status, decimal Result)> cases)
    {
        var sums = new ClusteredSums();

        foreach ((DateTimeOffset firedAt, IdeaStatus status, decimal result) in cases)
        {
            sums.Add(firedAt, status, result);
        }

        return sums.Statistics();
    }

    private static IEnumerable<DateTimeOffset> Selection()
    {
        return Enumerable.Range(0, 40).Select(index => Now.AddDays(-90).AddHours(36 * index).AddMinutes(30));
    }

    private static IEnumerable<DateTimeOffset> Check()
    {
        return Enumerable.Range(0, 40).Select(index => Now.AddDays(-30).AddHours(18 * index).AddMinutes(30));
    }

    private static IReadOnlyList<IdeaSignal> Signals()
    {
        int grid = ExitRuleGrid.Of(Interval.FifteenMinutes).Count;
        IEnumerable<SignalPath> selection = Selection().Select((firedAt, index) => Signal(index, firedAt) with
        {
            LadderAfter = index % 2 == 0 ? [.. Enumerable.Repeat((int?)null, 10)] : [1400, 1400, 1400, 1400, 1400, 1400, null, null, null, null],
            StopAfter = index % 2 == 0 ? 1000 : 3000,
            Closes = [.. Enumerable.Repeat(100m, grid)]
        });
        IEnumerable<SignalPath> check = Check().Select((firedAt, index) => Signal(40 + index, firedAt) with
        {
            LadderAfter = [.. Enumerable.Repeat((int?)null, 10)],
            StopAfter = index < 30 ? 1000 : null,
            Closes = [.. Enumerable.Repeat(index < 30 ? 100m : 101m, grid)]
        });

        return [.. selection.Concat(check).Select((path, index) =>
            IdeaSignalGenerator.Generate(path.FiredAt, instrument: new Instrument(MarketCategory.Linear, $"P{index}USDT")) with { Path = path })];
    }

    private static SignalPath Signal(int index, DateTimeOffset firedAt)
    {
        return IdeaSignalGenerator.Generate(firedAt).Path with { TrackedUntil = firedAt.AddDays(8) };
    }
}