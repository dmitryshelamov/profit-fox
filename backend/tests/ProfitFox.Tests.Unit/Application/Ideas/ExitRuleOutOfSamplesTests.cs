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
///             the check, 40 signals 12 h apart from 30 days ago, all before the end of the window 7 days and a minute before now: no level;
///             30 stop at 1000 minutes, the last 10 never stop and close at 101 (+0.5 R);
///         </item>
///         <item>3 signals in the last 7 days, after the end of the window: they count in no part.</item>
///     </list>
///     On the selection 96 · 3 R and every longer lifetime at 3 R take +3 R on the odd half and −1 R on the even: +1 R, the best, the shortest
///     first; the longer lifetimes leave out the signals within their horizon before the check, which never lifts them above +1 R. The
///     candidate 24 · 2 R expires everything at its close.
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

        ExitRuleOutOfSample check = ExitRuleOutOfSamples.Compute(signals, Candidate, _ => new FeeRates(0m, 0m), Now, CancellationToken.None);

        check.Window.ShouldBe(new OutOfSampleWindow(Now.AddDays(-90), Now.AddDays(-30), Now.AddDays(-7).AddMinutes(-1)));
        check.Rules.ShouldBe(171);
        check.Best!.Rule.ShouldBe(Candidate with { LifeCandles = 96, TargetR = 3m });
        check.Best.Parts.ShouldBe(new ExitRuleParts(
            Now.AddDays(-31).AddMinutes(-1),
            Statistics(Selection().Select((firedAt, index) => (firedAt, index % 2 == 0 ? IdeaStatus.Stop : IdeaStatus.Target, index % 2 == 0 ? -1m : 3m))),
            Statistics(Check().Select((firedAt, index) => (firedAt, index < 30 ? IdeaStatus.Stop : IdeaStatus.Expired, index < 30 ? -1m : 0.5m)))));
        check.Best.Parts.Selection!.Expectancy.ShouldBe(1m);
        check.Best.Parts.Check!.Expectancy.ShouldBe(-0.625m);
        check.Candidate.ShouldBe(new ExitRuleParts(
            Now.AddDays(-30).AddHours(-6).AddMinutes(-1),
            Statistics(Selection().Select(firedAt => (firedAt, IdeaStatus.Expired, 0m))),
            Statistics(Check().Select((firedAt, index) => (firedAt, IdeaStatus.Expired, index < 30 ? 0m : 0.5m)))));
        check.Overfitting.ShouldBe(OverfittingProbability.Of(ExitRuleGridPass.Sums(
            [.. signals.Select(signal => (new FeeRates(0m, 0m), (IReadOnlyList<SignalPath>)[signal.Path]))], ExitRuleGridPass.Rules(Candidate), check.Window,
            CancellationToken.None)));
        check.Overfitting.ShouldNotBeNull();
    }

    // Scenario "Мало входов на подборе": 10 signals of the selection and none of the check.
    [Test]
    public void Check_without_30_entries_has_no_best_rule_and_no_overfitting()
    {
        IdeaSignal[] signals = [.. Signals().Take(10)];

        ExitRuleOutOfSample check = ExitRuleOutOfSamples.Compute(signals, Candidate, _ => new FeeRates(0m, 0m), Now, CancellationToken.None);

        check.Best.ShouldBeNull();
        check.Overfitting.ShouldBeNull();
        check.Candidate.Check.ShouldBeNull();
        check.Candidate.Selection!.ExpectancyLow.ShouldBeNull();
    }

    // Finding 9 of the review: each entry pays the fees of its category, and a pullback without an entry counts in no part. A linear and a
    // spot pullback enter and expire at 101 (+0.5 R before the fees) a day apart on the selection; a third never enters.
    [Test]
    public void Parts_count_the_entries_with_the_fees_of_their_category_and_no_signal_without_an_entry()
    {
        IdeaExitRule pullback = new IdeaExitRules([]).Of(IdeaType.Pullback, Interval.FifteenMinutes);
        DateTimeOffset firedAt = Now.AddDays(-60);
        IdeaSignal Pullback(MarketCategory category, int day, int? entryAfter)
        {
            IdeaSignal signal = IdeaSignalGenerator.Generate(firedAt.AddDays(day), IdeaStatus.Expired, new Instrument(category, $"P{day}USDT"), type: IdeaType.Pullback);

            return signal with { Path = signal.Path with { EntryAfter = entryAfter, Closes = [.. signal.Path.Closes.Select(_ => 101m)] } };
        }

        IdeaSignal[] signals = [Pullback(MarketCategory.Linear, 0, 5), Pullback(MarketCategory.Spot, 1, 5), Pullback(MarketCategory.Linear, 2, null)];
        Func<MarketCategory, FeeRates> fees = category => category == MarketCategory.Spot ? new FeeRates(0.1m, 0.2m) : new FeeRates(0.02m, 0.05m);

        ExitRuleOutOfSample check = ExitRuleOutOfSamples.Compute(signals, pullback, fees, Now, CancellationToken.None);

        // A limit entry at 100 pays the maker fee, the exit at 101 the taker fee, over the risk of 2.
        decimal linear = (1m - ((0.02m * 100m) + (0.05m * 101m)) / 100m) / 2m;
        decimal spot = (1m - ((0.1m * 100m) + (0.2m * 101m)) / 100m) / 2m;
        check.Candidate.Selection.ShouldBe(Statistics([(firedAt, IdeaStatus.Expired, linear), (firedAt.AddDays(1), IdeaStatus.Expired, spot)]));
    }

    // Finding 4 of the review: a request cancelled before the check ends it.
    [Test]
    public void Check_of_a_cancelled_request_stops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Should.Throw<OperationCanceledException>(() => ExitRuleOutOfSamples.Compute(Signals(), Candidate, _ => new FeeRates(0m, 0m), Now, cancel.Token));
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
        return Enumerable.Range(0, 40).Select(index => Now.AddDays(-30).AddHours(12 * index).AddMinutes(30));
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

        IEnumerable<SignalPath> late = Enumerable.Range(0, 3).Select(index => Signal(80 + index, Now.AddDays(-6 + index)) with { StopAfter = 10, LadderAfter = [.. Enumerable.Repeat((int?)null, 10)], Closes = [] });

        return [.. selection.Concat(check).Concat(late).Select((path, index) =>
            IdeaSignalGenerator.Generate(path.FiredAt, instrument: new Instrument(MarketCategory.Linear, $"P{index}USDT")) with { Path = path })];
    }

    private static SignalPath Signal(int index, DateTimeOffset firedAt)
    {
        return IdeaSignalGenerator.Generate(firedAt).Path with { TrackedUntil = firedAt.AddDays(8) };
    }
}