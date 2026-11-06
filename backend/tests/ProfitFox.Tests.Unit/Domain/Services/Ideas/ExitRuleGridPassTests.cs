using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirement "Сетка проверки" of check-idea-exit-rules-out-of-sample: the rules the out-of-sample check goes through, and the entries and
///     their total R by every rule of the grid in the 6 blocks of 15 days from the start of the window (decision 4). The signals are on 15
///     minutes, a long entered at 100 with the stop at 98, the start of the window 2024-03-01.
/// </summary>
public class ExitRuleGridPassTests
{
    private static readonly IdeaExitRules Defaults = new IdeaExitRules([]);
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);

    // Scenario "Сетка «Отката» на 4 ч": 11 lifetimes × 9 targets, every one with the window of the candidate, the lifetimes first.
    [Test]
    public void Grid_of_a_pullback_is_every_lifetime_by_every_target_at_the_window_of_the_candidate()
    {
        IdeaExitRule candidate = Defaults.Of(IdeaType.Pullback, Interval.FourHours) with { EntryCandles = 8 };

        IReadOnlyList<IdeaExitRule> rules = ExitRuleGridPass.Rules(candidate);

        rules.Count.ShouldBe(99);
        rules.Select(rule => (rule.LifeCandles, rule.TargetR)).Take(10).ShouldBe(
            [(1, 1m), (1, 1.5m), (1, 2m), (1, 2.5m), (1, 3m), (1, 3.5m), (1, 4m), (1, 4.5m), (1, 5m), (2, 1m)]);
        rules[^1].ShouldBe(new IdeaExitRule(IdeaType.Pullback, Interval.FourHours, 42, 5m, 8, null));
        rules.ShouldAllBe(rule => rule.EntryCandles == 8 && rule.Type == IdeaType.Pullback && rule.Interval == Interval.FourHours);
    }

    // Scenario "Сетка «Уровней»": the lifetimes alone.
    [Test]
    public void Grid_of_levels_is_every_lifetime()
    {
        IdeaExitRule candidate = Defaults.Of(IdeaType.Levels, Interval.FifteenMinutes);

        ExitRuleGridPass.Rules(candidate).ShouldBe(
            [.. ExitRuleGrid.Of(Interval.FifteenMinutes).Select(life => new IdeaExitRule(IdeaType.Levels, Interval.FifteenMinutes, life, null, null, null))]);
        ExitRuleGrid.Of(Interval.FifteenMinutes).Count.ShouldBe(19);
    }

    // A reference set by hand, «Цель 2R» without fees, two rules: 1 · 2 R and 48 · 2 R.
    //   A, day 1 (block 0): +2 R at 40 minutes. By one candle it expires at the close 100.5, +0.25 R; by 48 it takes +2 R.
    //   B, the same key 1 h later (block 0), after A closed by either rule: the stop at 30 minutes, so one candle expires it at +0.25 R, 48 stop
    //   it at −1 R.
    //   C, day 20 (block 1): the stop at 5 minutes, −1 R by both.
    //   D, day 89 (block 5), another key: the path known for 20 minutes only. One candle decides it, +0.25 R; 48 candles do not.
    [Test]
    public void Sums_are_the_entries_and_their_total_r_of_each_block()
    {
        IdeaExitRule shortest = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes) with { LifeCandles = 1 };
        IdeaExitRule longer = shortest with { LifeCandles = 48 };
        (FeeRates, IReadOnlyList<SignalPath>)[] keys =
        [
            (new FeeRates(0m, 0m), [Market(From.AddDays(1), ladder: [(3, 40)]), Market(From.AddDays(1).AddHours(1), stop: 30), Market(From.AddDays(20), stop: 5)]),
            (new FeeRates(0m, 0m), [Market(From.AddDays(89), trackedFor: 20)])
        ];

        BlockSums[][] sums = ExitRuleGridPass.Sums(keys, [shortest, longer], From);

        sums[0].ShouldBe([new BlockSums(2, 0.5m), new BlockSums(1, -1m), default, default, default, new BlockSums(1, 0.25m)]);
        sums[1].ShouldBe([new BlockSums(2, 1m), new BlockSums(1, -1m), default, default, default, default]);
    }

    // Decision 4: the keys go to 8 parts by their number, key k to part k mod 8, and the parts add up in their order. Decimal sums round, so
    // the order is the answer: keys 0 and 8 (+1/3 R each) add up first, then key 1 (+10 R); one key after another would give another sum.
    [Test]
    public void Sums_of_the_keys_add_up_part_by_part_in_their_order()
    {
        IdeaExitRule rule = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes) with { LifeCandles = 1 };
        SignalPath third = Market(From, closes: [101m]) with { Stop = 97m };
        SignalPath ten = third with { Closes = [130m] };
        (FeeRates, IReadOnlyList<SignalPath>)[] keys =
        [
            .. Enumerable.Range(0, 9).Select(key => (new FeeRates(0m, 0m), (IReadOnlyList<SignalPath>)(key switch { 0 or 8 => [third], 1 => [ten], _ => [] })))
        ];

        BlockSums[][] sums = ExitRuleGridPass.Sums(keys, [rule], From);

        decimal parts = (1m / 3 + (1m / 3)) + 10m;
        sums[0][0].ShouldBe(new BlockSums(3, parts));
        (1m / 3 + 10m + (1m / 3)).ShouldNotBe(parts);
    }

    // Decision 4: every rule of the grid sums what the outcomes of its own pass and their results sum, on a set with every branch.
    [TestCaseSource(nameof(Sets))]
    public void Sums_of_every_rule_of_the_grid_agree_with_the_outcomes_of_its_own_pass(IdeaExitRule candidate, (FeeRates, IReadOnlyList<SignalPath>)[] keys)
    {
        IReadOnlyList<IdeaExitRule> rules = ExitRuleGridPass.Rules(candidate);

        BlockSums[][] sums = ExitRuleGridPass.Sums(keys, rules, From);

        for (int index = 0; index < rules.Count; index++)
        {
            sums[index].ShouldBe(Expected(keys, rules[index]), $"{rules[index]}");
        }
    }

    private static IEnumerable<TestCaseData> Sets()
    {
        IdeaExitRule market = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes);
        yield return new TestCaseData(market, new (FeeRates, IReadOnlyList<SignalPath>)[]
        {
            (Fees,
            [
                // Climbs the ladder to +3 R and stops at 10 h.
                Market(From.AddDays(1), ladder: [(0, 10), (1, 20), (2, 30), (3, 40), (4, 300), (5, 420)], stop: 600, closes: Rising()),
                // Repeats while the one before is open at the longer lifetimes.
                Market(From.AddDays(1).AddHours(2), ladder: [(0, 15)], stop: 2000, closes: Rising()),
                // Never stops, no level, expires by the closes.
                Market(From.AddDays(20), closes: Rising()),
                // The stop in the first minute.
                Market(From.AddDays(50), stop: 1),
                // A market signal that did not enter: counts, with no result.
                Market(From.AddDays(55)) with { EntryAfter = null, TrackedUntil = From.AddDays(55).AddMinutes(1), Closes = [] },
                // The path is known for 3 h: the longer lifetimes stop the key here.
                Market(From.AddDays(85), trackedFor: 180, closes: Rising()),
                Market(From.AddDays(86), ladder: [(0, 5), (1, 8)], stop: 9)
            ]),
            (new FeeRates(0.1m, 0.1m), [Market(From.AddDays(30), ladder: [(0, 100), (1, 2000)], closes: Rising()), Market(From.AddDays(61), stop: 50)])
        });

        IdeaExitRule stopEntry = Defaults.Of(IdeaType.StopEntry, Interval.FifteenMinutes);
        yield return new TestCaseData(stopEntry, new (FeeRates, IReadOnlyList<SignalPath>)[]
        {
            (Fees,
            [
                // Enters at 30 minutes, inside the window of 12 candles.
                Market(From.AddDays(2), ladder: [(1, 50), (3, 700)], stop: 900, closes: Rising(), type: IdeaType.StopEntry) with { EntryAfter = 30 },
                // Enters after the window: expires without an entry at its end.
                Market(From.AddDays(10), ladder: [(1, 50)], closes: Rising(), type: IdeaType.StopEntry) with { EntryAfter = 200 },
                // The stop before the entry, inside the window.
                Market(From.AddDays(40), type: IdeaType.StopEntry) with { EntryAfter = null, StopBeforeEntryAfter = 50, TrackedUntil = From.AddDays(40).AddMinutes(50), Closes = [] },
                Market(From.AddDays(70), ladder: [(0, 20)], stop: 40, type: IdeaType.StopEntry) with { EntryAfter = 5 },
                // No entry yet, and the window is not over: the key stops here.
                Market(From.AddDays(89), type: IdeaType.StopEntry) with { EntryAfter = null, TrackedUntil = From.AddDays(89).AddMinutes(60), Closes = [] },
                Market(From.AddDays(89).AddHours(5), stop: 3, type: IdeaType.StopEntry) with { EntryAfter = 1 }
            ])
        });

        IdeaExitRule levels = Defaults.Of(IdeaType.Levels, Interval.FifteenMinutes);
        yield return new TestCaseData(levels, new (FeeRates, IReadOnlyList<SignalPath>)[]
        {
            (Fees,
            [
                Market(From.AddDays(3), ladder: [(0, 10)], stop: 5000, closes: Rising(), type: IdeaType.Levels) with { Target = 103.5m, TargetAfter = 400 },
                Market(From.AddDays(33), stop: 70, closes: Rising(), type: IdeaType.Levels) with { Target = 101m },
                Market(From.AddDays(63), closes: Rising(), type: IdeaType.Levels) with { Target = 102m, TargetAfter = 60 }
            ])
        });
    }

    private static BlockSums[] Expected((FeeRates, IReadOnlyList<SignalPath>)[] keys, IdeaExitRule rule)
    {
        var expected = new BlockSums[ExitRuleGridPass.Blocks];

        foreach ((FeeRates fees, IReadOnlyList<SignalPath> paths) in keys)
        {
            foreach ((SignalPath path, IdeaTrack outcome) in ExitOutcomes.Counted(paths, rule))
            {
                if (CaseResult.R(outcome, fees) is { } result)
                {
                    int block = (int)((path.FiredAt - From) / TimeSpan.FromDays(15));
                    expected[block] = new BlockSums(expected[block].Entries + 1, expected[block].TotalR + result);
                }
            }
        }

        return expected;
    }

    // The closes of the grid from 100.1 up by 0.37: every lifetime expires at its own price.
    private static decimal[] Rising()
    {
        return [.. ExitRuleGrid.Of(Interval.FifteenMinutes).Select((_, index) => 100.1m + (index * 0.37m))];
    }

    private static SignalPath Market(
        DateTimeOffset firedAt,
        (int Level, int After)[]? ladder = null,
        int? stop = null,
        int trackedFor = 14 * 24 * 60,
        decimal[]? closes = null,
        IdeaType type = IdeaType.TwoR)
    {
        var touches = new int?[10];

        foreach ((int level, int after) in ladder ?? [])
        {
            touches[level] = after;
        }

        return SignalPath.Fired(new IdeaPlan(type, IdeaSide.Long, 100m, 98m, 104m), Interval.FifteenMinutes, firedAt, 0.01m) with
        {
            EntryAfter = 1,
            StopAfter = stop,
            LadderAfter = touches,
            Closes = closes ?? [.. ExitRuleGrid.Of(Interval.FifteenMinutes).Select(_ => 100.5m)],
            TrackedUntil = firedAt.AddMinutes(trackedFor)
        };
    }
}