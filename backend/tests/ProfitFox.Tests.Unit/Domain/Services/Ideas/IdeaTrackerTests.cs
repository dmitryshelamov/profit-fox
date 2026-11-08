using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Вход идеи" and "Исход идеи": an idea on 5 minute candles fired at the end of the minute 10:00, stepped minute by minute.
///     A market long plans 105 with the stop at 100.
/// </summary>
public class IdeaTrackerTests
{
    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2024-03-01T10:01:00Z");
    private static readonly IdeaPlan TwoR = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 105m, 100m, 115m);
    private static readonly IdeaPlan Pullback = new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 104m, 100m, 112m);
    private static readonly IdeaPlan StopEntry = new IdeaPlan(IdeaType.StopEntry, IdeaSide.Long, 102.01m, 99.5m, 107.03m);

    // Decision 4: the entry is the open of the next minute, and a target of 2R follows it.
    [Test]
    public void Market_idea_enters_at_the_open_of_the_next_minute()
    {
        IdeaTrack track = Run(TwoR, Minute(0, 106m, 107m, 105.5m, 106.5m));

        track.ShouldBe(new IdeaTrack(TwoR with { Entry = 106m, Target = 118m }, Fired, 5, 24, 2m, null, IdeaStatus.Active, Fired, Fired));
    }

    // The target of 2R follows the entry onto the price step of the pair, towards the entry: 106.1 + 2 × 6.1 = 118.3 goes down to 118.25.
    [Test]
    public void Target_that_follows_the_entry_goes_on_the_price_step()
    {
        var shortPlan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 105m, 110m, 95m);

        Run(TwoR, 0.25m, Minute(0, 106.1m, 107m, 105.5m, 106.5m)).Plan.Target.ShouldBe(118.25m);
        Run(shortPlan, 0.25m, Minute(0, 104.1m, 104.5m, 103m, 104m)).Plan.Target.ShouldBe(92.5m);
    }

    [Test]
    public void Levels_keep_their_target_at_the_entry()
    {
        var levels = new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 105m, 100m, 115m);

        Run(levels, Minute(0, 106m, 107m, 105.5m, 106.5m)).Plan.ShouldBe(levels with { Entry = 106m });
    }

    [Test]
    public void Idea_reaches_its_target()
    {
        IdeaTrack track = Run(TwoR, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 105m, 106m, 104m, 105m), Minute(2, 110m, 115.5m, 109m, 113m));

        (track.Status, track.StatusAt, track.ExitPrice).ShouldBe((IdeaStatus.Target, Fired.AddMinutes(2), 115m));
    }

    [Test]
    public void Idea_reaches_its_stop()
    {
        IdeaTrack track = Run(TwoR, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 101m, 102m, 99.5m, 100.5m));

        (track.Status, track.StatusAt, track.ExitPrice).ShouldBe((IdeaStatus.Stop, Fired.AddMinutes(1), 100m));
    }

    // Scenario "Цель и стоп в одной минуте".
    [Test]
    public void A_minute_that_reaches_both_is_a_stop()
    {
        Run(TwoR, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 105m, 116m, 99m, 105m)).Status.ShouldBe(IdeaStatus.Stop);
    }

    [Test]
    public void Short_reaches_its_stop_above_and_its_target_below()
    {
        var shortPlan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 105m, 110m, 95m);

        Run(shortPlan, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 108m, 110m, 107m, 109m)).Status.ShouldBe(IdeaStatus.Stop);
        Run(shortPlan, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 100m, 101m, 95m, 96m)).Status.ShouldBe(IdeaStatus.Target);
    }

    // 24 candles of 5 minutes from the entry: the minute that ends 120 minutes after it closes the idea at its close.
    [Test]
    public void Idea_that_reaches_neither_expires_by_the_close_after_24_candles()
    {
        IdeaCandle[] minutes = [.. Enumerable.Range(0, 120).Select(index => Minute(index, 105m, 106m, 104m, index == 119 ? 105.5m : 105m))];

        IdeaTrack beforeLast = Run(TwoR, minutes[..119]);
        IdeaTrack track = Run(TwoR, minutes);

        beforeLast.Status.ShouldBe(IdeaStatus.Active);
        (track.Status, track.StatusAt, track.ExitPrice).ShouldBe((IdeaStatus.Expired, Fired.AddMinutes(120), 105.5m));
    }

    // A market idea that would enter beyond its stop has no risk to measure: it expires without an entry.
    [Test]
    public void Market_idea_that_opens_beyond_its_stop_expires_without_an_entry()
    {
        IdeaTrack track = Run(TwoR, Minute(0, 99m, 100m, 98m, 99m));

        (track.Status, track.StatusAt, track.EnteredAt).ShouldBe((IdeaStatus.Expired, Fired, (DateTimeOffset?)null));
    }

    // Decision 10: «Уровни» whose next open is already at or beyond the target has nothing to gain; it expires without an entry.
    [Test]
    public void Levels_idea_that_opens_beyond_its_target_expires_without_an_entry()
    {
        var levels = new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 105m, 100m, 115m);

        IdeaTrack track = Run(levels, Minute(0, 116m, 117m, 115.5m, 116m));

        (track.Status, track.StatusAt, track.EnteredAt).ShouldBe((IdeaStatus.Expired, Fired, (DateTimeOffset?)null));
    }

    // A buy limit fills when a minute's low touches it; that minute may still reach the stop, while its target only counts from the next.
    [Test]
    public void Pullback_enters_when_a_minute_touches_its_entry()
    {
        IdeaTrack track = Run(Pullback, Minute(0, 106m, 106m, 105m, 105m), Minute(1, 105m, 113m, 104m, 104.5m));

        (track.Status, track.EnteredAt, track.Plan).ShouldBe((IdeaStatus.Active, Fired.AddMinutes(1), Pullback));
        Run(Pullback, Minute(0, 106m, 106m, 103m, 103m), Minute(1, 103m, 113m, 102m, 112.5m)).Status.ShouldBe(IdeaStatus.Target);
    }

    [Test]
    public void Pullback_that_touches_its_entry_and_stop_in_one_minute_is_a_stop()
    {
        Run(Pullback, Minute(0, 105m, 105m, 99m, 100m)).Status.ShouldBe(IdeaStatus.Stop);
    }

    // Scenario "Откат без касания": 4 candles of 5 minutes.
    [Test]
    public void Pullback_without_a_touch_in_four_candles_expires_without_an_entry()
    {
        IdeaCandle[] minutes = [.. Enumerable.Range(0, 21).Select(index => Minute(index, 106m, 107m, 105m, 106m))];

        Run(Pullback, minutes[..20]).Status.ShouldBe(IdeaStatus.AwaitingEntry);
        Run(Pullback, minutes).ShouldBe(new IdeaTrack(Pullback, Fired, 5, 24, 2m, 4, IdeaStatus.Expired, Fired.AddMinutes(20)));
    }

    // Requirement "Вход «Стоп-входа»": a minute that reaches the entry fills the stop order at it.
    [Test]
    public void Stop_entry_enters_when_a_minute_reaches_its_entry()
    {
        IdeaTrack track = Run(StopEntry, 0.01m, Minute(0, 101m, 101.5m, 100.5m, 101.2m), Minute(1, 101.8m, 102.1m, 101.7m, 102m));

        (track.Status, track.EnteredAt, track.Plan).ShouldBe((IdeaStatus.Active, Fired.AddMinutes(1), StopEntry));
    }

    // A minute whose high is exactly the entry fills the stop order.
    [Test]
    public void Stop_entry_enters_when_a_minute_touches_its_entry_exactly()
    {
        Run(StopEntry, 0.01m, Minute(0, 101.8m, 102.01m, 101.7m, 102m)).Status.ShouldBe(IdeaStatus.Active);
    }

    // Scenario "Вход с гэпом": the minute opened at 102.30, beyond the entry; the target of 2R follows it, 102.30 + 2 × 2.80.
    [Test]
    public void Stop_entry_with_a_gap_enters_at_the_open_and_targets_two_r_from_it()
    {
        IdeaTrack track = Run(StopEntry, 0.01m, Minute(0, 102.30m, 102.6m, 102.2m, 102.5m));

        (track.Status, track.Plan).ShouldBe((IdeaStatus.Active, StopEntry with { Entry = 102.30m, Target = 107.90m }));
    }

    // The target follows the entry by the target of the idea's own rule: 102.30 + 3 × 2.80, and 106 + 3 × 6 at the market.
    [Test]
    public void Target_that_follows_the_entry_takes_the_target_of_the_rule()
    {
        var stopEntry = IdeaTrack.Fired(StopEntry, Fired, 5, Rule(IdeaType.StopEntry) with { TargetR = 3m }, 0.01m);
        var market = IdeaTrack.Fired(TwoR, Fired, 5, Rule(IdeaType.TwoR) with { TargetR = 3m });

        IdeaTracker.Step(stopEntry, Minute(0, 102.30m, 102.6m, 102.2m, 102.5m)).Plan.Target.ShouldBe(110.70m);
        IdeaTracker.Step(market, Minute(0, 106m, 107m, 105.5m, 106.5m)).Plan.Target.ShouldBe(124m);
    }

    [Test]
    public void Stop_entry_short_with_a_gap_enters_at_the_open()
    {
        var shortPlan = new IdeaPlan(IdeaType.StopEntry, IdeaSide.Short, 99.99m, 102.5m, 94.97m);

        Run(shortPlan, 0.01m, Minute(0, 99.80m, 99.9m, 99.5m, 99.6m)).Plan.ShouldBe(shortPlan with { Entry = 99.80m, Target = 94.40m });
    }

    // Scenario "Пробой в другую сторону": the stop before the entry; no entry, no case.
    [Test]
    public void Stop_entry_that_reaches_its_stop_before_the_entry_expires_without_an_entry()
    {
        IdeaTrack track = Run(StopEntry, 0.01m, Minute(0, 101m, 101.2m, 99.4m, 99.6m));

        track.ShouldBe(new IdeaTrack(StopEntry, Fired, 5, 24, 2m, 12, IdeaStatus.Expired, Fired, Tick: 0.01m));
    }

    // Within the minute of the fill the target may have come before it; only the stop counts.
    [Test]
    public void Stop_entry_that_reaches_its_entry_and_stop_in_one_minute_is_a_stop()
    {
        IdeaTrack track = Run(StopEntry, 0.01m, Minute(0, 101.5m, 107.5m, 99.4m, 100m));

        (track.Status, track.EnteredAt, track.ExitPrice).ShouldBe((IdeaStatus.Stop, Fired, 99.5m));
    }

    // Scenario "Без входа": 12 candles of 5 minutes.
    [Test]
    public void Stop_entry_without_an_entry_in_twelve_candles_expires()
    {
        IdeaCandle[] minutes = [.. Enumerable.Range(0, 61).Select(index => Minute(index, 101m, 101.5m, 100.5m, 101m))];

        Run(StopEntry, minutes[..60]).Status.ShouldBe(IdeaStatus.AwaitingEntry);
        Run(StopEntry, minutes).ShouldBe(new IdeaTrack(StopEntry, Fired, 5, 24, 2m, 12, IdeaStatus.Expired, Fired.AddMinutes(60)));
    }

    [Test]
    public void A_closed_idea_stays_as_it_is()
    {
        IdeaTrack stopped = Run(TwoR, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 101m, 102m, 99.5m, 100.5m));

        IdeaTracker.Step(stopped, Minute(2, 120m, 130m, 110m, 125m)).ShouldBe(stopped);
    }

    // Fix after review: a pair out of trading takes its open ideas with it; an active one leaves at the last close known.
    [Test]
    public void Idea_of_a_pair_out_of_trading_expires_where_it_stands()
    {
        IdeaTrack active = Run(TwoR, Minute(0, 105m, 106m, 104m, 105m));
        IdeaTrack waiting = Track(Pullback);

        IdeaTracker.Expire(active, Fired.AddMinutes(30), 104.5m).ShouldBe(active with { Status = IdeaStatus.Expired, StatusAt = Fired.AddMinutes(30), ExitPrice = 104.5m });
        IdeaTracker.Expire(waiting, Fired.AddMinutes(30), 104.5m).ShouldBe(waiting with { Status = IdeaStatus.Expired, StatusAt = Fired.AddMinutes(30) });
    }

    // Scenario "Срок 48 свечей": 48 candles of 15 minutes, 12 hours from the entry.
    [Test]
    public void Idea_lives_the_candles_of_its_own_rule()
    {
        var fired = IdeaTrack.Fired(TwoR, Fired, 15, Rule(IdeaType.TwoR) with { LifeCandles = 48 });
        IdeaCandle[] minutes = [.. Enumerable.Range(0, 720).Select(index => Minute(index, 105m, 106m, 104m, 105m))];

        minutes[..719].Aggregate(fired, IdeaTracker.Step).Status.ShouldBe(IdeaStatus.Active);
        IdeaTrack track = minutes.Aggregate(fired, IdeaTracker.Step);
        (track.Status, track.StatusAt).ShouldBe((IdeaStatus.Expired, Fired.AddMinutes(720)));
    }

    // Scenario "Окно меняется после создания": the window is the idea's own, 8 candles of 5 minutes here.
    [Test]
    public void Pullback_waits_the_window_of_its_own_rule()
    {
        var fired = IdeaTrack.Fired(Pullback, Fired, 5, Rule(IdeaType.Pullback) with { EntryCandles = 8 });
        IdeaCandle[] minutes = [.. Enumerable.Range(0, 41).Select(index => Minute(index, 106m, 107m, 105m, 106m))];

        minutes[..40].Aggregate(fired, IdeaTracker.Step).Status.ShouldBe(IdeaStatus.AwaitingEntry);
        minutes.Aggregate(fired, IdeaTracker.Step).StatusAt.ShouldBe(Fired.AddMinutes(40));
        IdeaTracker.EntryDeadline(fired).ShouldBe(Fired.AddMinutes(40));
    }

    [Test]
    public void Stop_entry_waits_the_window_of_its_own_rule()
    {
        var fired = IdeaTrack.Fired(StopEntry, Fired, 5, Rule(IdeaType.StopEntry) with { EntryCandles = 3 });
        IdeaCandle[] minutes = [.. Enumerable.Range(0, 16).Select(index => Minute(index, 101m, 101.5m, 100.5m, 101m))];

        minutes[..15].Aggregate(fired, IdeaTracker.Step).Status.ShouldBe(IdeaStatus.AwaitingEntry);
        minutes.Aggregate(fired, IdeaTracker.Step).Status.ShouldBe(IdeaStatus.Expired);
    }

    // Requirement "Конец идеи у сделки в работе": 16 candles of 15 minutes from the entry at 10:00 end at 14:00.
    [Test]
    public void Life_of_an_entered_idea_ends_its_candles_after_the_entry()
    {
        var entered = DateTimeOffset.Parse("2024-03-01T10:00:00Z");
        var track = new IdeaTrack(TwoR, Fired, 15, 16, 2m, null, IdeaStatus.Active, entered, entered);

        track.LifeEndsAt.ShouldBe(DateTimeOffset.Parse("2024-03-01T14:00:00Z"));
    }

    [Test]
    public void Idea_without_an_entry_has_no_end_of_life()
    {
        Track(Pullback).LifeEndsAt.ShouldBeNull();
    }

    private static IdeaTrack Run(IdeaPlan plan, params IdeaCandle[] minutes)
    {
        return minutes.Aggregate(Track(plan), IdeaTracker.Step);
    }

    private static IdeaTrack Run(IdeaPlan plan, decimal tick, params IdeaCandle[] minutes)
    {
        return minutes.Aggregate(IdeaTrack.Fired(plan, Fired, 5, Rule(plan.Type), tick), IdeaTracker.Step);
    }

    // The track by the default rule of its type.
    private static IdeaTrack Track(IdeaPlan plan)
    {
        return IdeaTrack.Fired(plan, Fired, 5, Rule(plan.Type));
    }

    private static IdeaExitRule Rule(IdeaType type)
    {
        return new IdeaExitRules([]).Of(type, Interval.FiveMinutes);
    }

    private static IdeaCandle Minute(int index, decimal open, decimal high, decimal low, decimal close)
    {
        return new IdeaCandle(Fired.AddMinutes(index), open, high, low, close, 100m);
    }
}