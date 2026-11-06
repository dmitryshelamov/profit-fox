using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Decision 3: on the same minutes a live idea by a rule and the outcome of the path of the same signal by that rule are one outcome — the
///     same status, time, entry, exit and levels of an entered idea — and while the idea is open the path has none. Signals on 15 minute candles fired at 10:00.
/// </summary>
public class ExitOutcomesAgreementTests
{
    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2024-03-01T10:00:00Z");
    private const decimal Tick = 0.01m;

    // Lifetimes and targets of the grid: the reference path reaches +0.5 R at 30 minutes, +1 R at 61 and the stop at 121.
    [TestCase(4, 1)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(8, 2)]
    [TestCase(12, 0.5)]
    [TestCase(1, 0.5)]
    public void Target_stop_and_expiry_agree(int life, decimal target)
    {
        Agree(Plan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, target), Rule(IdeaType.TwoR, life, target), IdeaFixtures.PathToStop());
    }

    [TestCase(1)]
    [TestCase(3)]
    public void Target_and_stop_in_one_minute_agree(decimal target)
    {
        IdeaCandle[] minutes = [Minute(0, 100m, 100.5m, 99.5m, 100m), Minute(1, 100m, 108m, 97m, 99m), Minute(2, 99m, 99m, 96m, 96m)];

        Agree(Plan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, target), Rule(IdeaType.TwoR, 24, target), minutes);
    }

    // Scenario "Вход с гэпом": 102.30 opens beyond the stop order of 102.01; +1 R from it is 105.10, +2 R 107.90.
    [TestCase(24, 1, 12)]
    [TestCase(24, 2, 12)]
    [TestCase(2, 2, 12)]
    [TestCase(24, 2, 1)]
    public void Stop_entry_with_a_gap_agrees(int life, decimal target, int window)
    {
        IdeaCandle[] minutes =
        [
            Minute(0, 101m, 101.5m, 100.5m, 101m), .. Flat(1, 14, 101m), Minute(15, 102.30m, 102.6m, 102.2m, 102.5m), .. Flat(16, 20, 103m),
            Minute(36, 103m, 105.2m, 103m, 105m), .. Flat(37, 20, 105m), Minute(57, 105m, 108m, 104.9m, 107.95m), .. Flat(58, 10, 107m)
        ];

        Agree(Plan(IdeaType.StopEntry, IdeaSide.Long, 102.01m, 99.5m, target), Rule(IdeaType.StopEntry, life, target, window), minutes);
    }

    [TestCase(1)]
    [TestCase(12)]
    public void Stop_entry_stopped_before_its_entry_agrees(int window)
    {
        IdeaCandle[] minutes = [.. Flat(0, 30, 101m), Minute(30, 101m, 101.2m, 99.4m, 99.6m), .. Flat(31, 200, 99.6m)];

        Agree(Plan(IdeaType.StopEntry, IdeaSide.Long, 102.01m, 99.5m, 2m), Rule(IdeaType.StopEntry, 24, 2m, window), minutes);
    }

    // «Откат» without a touch, and one that fills after 70 minutes and reaches +1 R.
    [TestCase(4)]
    [TestCase(8)]
    public void Pullback_agrees(int window)
    {
        Agree(Plan(IdeaType.Pullback, IdeaSide.Short, 101m, 103m, 1m), Rule(IdeaType.Pullback, 24, 1m, window), Flat(0, 200, 100m));

        IdeaCandle[] filled = [.. Flat(0, 69, 100m), Minute(69, 100m, 101m, 100m, 100.8m), .. Flat(70, 20, 100.5m), Minute(90, 100.5m, 100.5m, 98.9m, 99.2m), .. Flat(91, 10, 99m)];
        Agree(Plan(IdeaType.Pullback, IdeaSide.Short, 101m, 103m, 1m), Rule(IdeaType.Pullback, 24, 1m, window), filled);
    }

    // «Уровни» go to their zone; beyond the zone at the open they do not enter.
    [TestCase(100)]
    [TestCase(103.5)]
    public void Levels_agree(decimal open)
    {
        IdeaCandle[] minutes = [Minute(0, open, open + 0.5m, open - 0.5m, open), .. Flat(1, 20, 101m), Minute(21, 101m, 103m, 101m, 102.9m), .. Flat(22, 5, 102m)];

        Agree(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 100m, 98m, 103m), Rule(IdeaType.Levels, 24, null), minutes);
    }

    private static void Agree(IdeaPlan plan, IdeaExitRule rule, IReadOnlyList<IdeaCandle> minutes)
    {
        // Each prefix of the minutes: an open idea has no outcome yet, a closed one the same.
        for (int count = 1; count <= minutes.Count; count++)
        {
            IdeaTrack track = minutes.Take(count).Aggregate(IdeaTrack.Fired(plan, Fired, 15, rule, Tick), IdeaTracker.Step);
            SignalPath path = minutes.Take(count).Aggregate(SignalPath.Fired(plan, Interval.FifteenMinutes, Fired, Tick), SignalPathTracker.Step);
            IdeaTrack? outcome = ExitOutcomes.Of(path, rule);

            if (track.Status is IdeaStatus.AwaitingEntry or IdeaStatus.Active)
            {
                outcome.ShouldBeNull($"after {count} minutes");
            }
            else if (track.EnteredAt is null)
            {
                // Without an entry the levels mean nothing; a path keeps the open of a stop entry that gapped after its window.
                (outcome.ShouldNotBeNull($"after {count} minutes") with { Plan = track.Plan }).ShouldBe(track, $"after {count} minutes");
            }
            else
            {
                outcome.ShouldBe(track, $"after {count} minutes");
            }
        }
    }

    private static IdeaPlan Plan(IdeaType type, IdeaSide side, decimal entry, decimal stop, decimal target)
    {
        return IdeaRules.InR(type, side, entry, stop, target, Tick);
    }

    private static IdeaExitRule Rule(IdeaType type, int life, decimal? target, int? window = null)
    {
        return new IdeaExitRule(type, Interval.FifteenMinutes, life, target, window, null);
    }

    private static IdeaCandle[] Flat(int from, int count, decimal price)
    {
        return [.. Enumerable.Range(from, count).Select(index => Minute(index, price, price, price, price))];
    }

    private static IdeaCandle Minute(int index, decimal open, decimal high, decimal low, decimal close)
    {
        return new IdeaCandle(Fired.AddMinutes(index), open, high, low, close, 100m);
    }
}