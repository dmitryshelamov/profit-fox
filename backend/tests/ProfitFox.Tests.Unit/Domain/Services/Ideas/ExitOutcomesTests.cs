using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Исход сигнала по правилу" and "Неполный путь": the outcome of a signal on 15 minute candles fired at 10:00 from its path,
///     by a rule. A long «Цель 2R» enters at the market at 100 with the stop at 98.
/// </summary>
public class ExitOutcomesTests
{
    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2024-03-01T10:00:00Z");
    private static readonly IdeaExitRules Defaults = new IdeaExitRules([]);

    // Scenario "Цель позже срока": +2 R at 18:00; 24 candles end at 16:00, by the close then; 48 candles take the target.
    [Test]
    public void Target_after_the_lifetime_expires_by_the_close_at_its_end()
    {
        SignalPath path = Market() with { LadderAfter = Ladder((3, 481)), Closes = Closes(19), TrackedUntil = Fired.AddDays(7) };
        IdeaExitRule rule = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes);

        IdeaTrack expired = ExitOutcomes.Of(path, rule)!;
        IdeaTrack target = ExitOutcomes.Of(path, rule with { LifeCandles = 48 })!;

        (expired.Status, expired.StatusAt, expired.EnteredAt, expired.ExitPrice).ShouldBe((IdeaStatus.Expired, Fired.AddHours(6), (DateTimeOffset?)Fired, (decimal?)109m));
        (target.Status, target.StatusAt, target.ExitPrice).ShouldBe((IdeaStatus.Target, Fired.AddHours(8), (decimal?)104m));
        target.Plan.ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m));
        (target.LifeCandles, target.TargetR, target.CandleMinutes, target.FiredAt).ShouldBe((48, (decimal?)2m, 15, Fired));
    }

    // The target of the rule is a level of the ladder: 3 R is the sixth.
    [Test]
    public void Target_of_the_rule_is_its_level_of_the_ladder()
    {
        SignalPath path = Market() with { LadderAfter = Ladder((3, 30), (5, 90)), StopAfter = 200, Closes = Closes(6), TrackedUntil = Fired.AddMinutes(200) };
        IdeaExitRule rule = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes);

        IdeaTrack three = ExitOutcomes.Of(path, rule with { TargetR = 3m })!;

        (three.Status, three.StatusAt, three.ExitPrice, three.Plan.Target).ShouldBe((IdeaStatus.Target, Fired.AddMinutes(89), (decimal?)106m, 106m));
        ExitOutcomes.Of(path, rule with { TargetR = 3.5m })!.Status.ShouldBe(IdeaStatus.Stop);
    }

    [Test]
    public void Stop_within_the_lifetime_is_a_stop()
    {
        SignalPath path = Market() with { StopAfter = 121, Closes = Closes(6), TrackedUntil = Fired.AddMinutes(121) };
        IdeaExitRule rule = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes);

        IdeaTrack stop = ExitOutcomes.Of(path, rule)!;

        (stop.Status, stop.StatusAt, stop.ExitPrice).ShouldBe((IdeaStatus.Stop, Fired.AddMinutes(120), (decimal?)98m));
        ExitOutcomes.Of(path, rule with { LifeCandles = 8 })!.ShouldSatisfyAllConditions(
            track => track.Status.ShouldBe(IdeaStatus.Expired),
            track => track.ExitPrice.ShouldBe(106m));
    }

    // Scenario "Откат с длинным окном": the limit filled at the minute that ended 2 hours after the firing.
    [Test]
    public void Pullback_enters_only_within_the_window_of_the_rule()
    {
        SignalPath path = Market() with { Type = IdeaType.Pullback, EntryAfter = 120, TrackedUntil = Fired.AddDays(1), Closes = Closes(19) };
        IdeaExitRule rule = Defaults.Of(IdeaType.Pullback, Interval.FifteenMinutes);

        IdeaTrack four = ExitOutcomes.Of(path, rule)!;

        (four.Status, four.StatusAt, four.EnteredAt).ShouldBe((IdeaStatus.Expired, Fired.AddMinutes(60), (DateTimeOffset?)null));
        (four.Plan, four.EntryCandles).ShouldBe((new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m), (int?)4));
        ExitOutcomes.Of(path, rule with { EntryCandles = 8 })!.EnteredAt.ShouldBe(Fired.AddMinutes(119));
    }

    // The stop of a stop entry before its entry window ends closes it without an entry then; after the window, the window ends it.
    [Test]
    public void Stop_entry_stopped_before_its_entry_expires_without_one()
    {
        SignalPath path = Market() with { Type = IdeaType.StopEntry, EntryAfter = null, StopBeforeEntryAfter = 100, TrackedUntil = Fired.AddMinutes(100) };
        IdeaExitRule rule = Defaults.Of(IdeaType.StopEntry, Interval.FifteenMinutes);

        ExitOutcomes.Of(path, rule)!.StatusAt.ShouldBe(Fired.AddMinutes(99));
        ExitOutcomes.Of(path, rule with { EntryCandles = 4 })!.StatusAt.ShouldBe(Fired.AddMinutes(60));
    }

    [Test]
    public void Market_signal_without_an_entry_expires_at_once()
    {
        SignalPath path = Market() with { EntryAfter = null, TrackedUntil = Fired.AddMinutes(1) };

        IdeaTrack track = ExitOutcomes.Of(path, Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes))!;

        (track.Status, track.StatusAt, track.EnteredAt).ShouldBe((IdeaStatus.Expired, Fired, (DateTimeOffset?)null));
    }

    // «Уровни» take their zone.
    [Test]
    public void Levels_reach_their_zone()
    {
        SignalPath path = Market() with { Type = IdeaType.Levels, Target = 103m, TargetAfter = 50, LadderAfter = Ladder((2, 40)), StopAfter = 70, TrackedUntil = Fired.AddMinutes(70) };

        IdeaTrack track = ExitOutcomes.Of(path, Defaults.Of(IdeaType.Levels, Interval.FifteenMinutes))!;

        (track.Status, track.ExitPrice, track.Plan.Target, track.TargetR).ShouldBe((IdeaStatus.Target, (decimal?)103m, 103m, (decimal?)null));
    }

    // Scenario "Свежий сигнал": entered 2 hours ago with neither level, a lifetime of 6 hours.
    [Test]
    public void Fresh_signal_has_no_outcome_yet()
    {
        SignalPath path = Market() with { TrackedUntil = Fired.AddHours(2) };
        IdeaExitRule rule = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes);

        ExitOutcomes.Of(path, rule).ShouldBeNull();
        ExitOutcomes.Of(path with { TrackedUntil = Fired.AddHours(6), Closes = Closes(9) }, rule)!.Status.ShouldBe(IdeaStatus.Expired);
        ExitOutcomes.Of(Market() with { EntryAfter = null, Type = IdeaType.Pullback, TrackedUntil = Fired.AddMinutes(60) }, Defaults.Of(IdeaType.Pullback, Interval.FifteenMinutes)).ShouldBeNull();
    }

    // A long «Цель 2R» entered at the open after the firing, without touches.
    private static SignalPath Market()
    {
        return SignalPath.Fired(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Interval.FifteenMinutes, Fired, 0.01m) with { EntryAfter = 1 };
    }

    // The ladder with the given touches by index: 0 is +0.5 R.
    private static int?[] Ladder(params (int Index, int After)[] touches)
    {
        var ladder = new int?[10];

        foreach ((int index, int after) in touches)
        {
            ladder[index] = after;
        }

        return ladder;
    }

    // The closes of the first points of the grid: 101, 102, … at 1, 2, 3, 4, 6, 8, 12, 16, 24 … candles.
    private static decimal[] Closes(int count)
    {
        return [.. Enumerable.Range(1, count).Select(index => 100m + index)];
    }
}