using ProfitFox.Domain.Entities.Ideas.Enums;
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

    // Decision 4: the entry is the open of the next minute, and a target of 2R follows it.
    [Test]
    public void Market_idea_enters_at_the_open_of_the_next_minute()
    {
        IdeaTrack track = Run(TwoR, Minute(0, 106m, 107m, 105.5m, 106.5m));

        track.ShouldBe(new IdeaTrack(TwoR with { Entry = 106m, Target = 118m }, Fired, 5, IdeaStatus.Active, Fired, Fired));
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
        Run(Pullback, minutes).ShouldBe(new IdeaTrack(Pullback, Fired, 5, IdeaStatus.Expired, Fired.AddMinutes(20)));
    }

    [Test]
    public void A_closed_idea_stays_as_it_is()
    {
        IdeaTrack stopped = Run(TwoR, Minute(0, 105m, 106m, 104m, 105m), Minute(1, 101m, 102m, 99.5m, 100.5m));

        IdeaTracker.Step(stopped, Minute(2, 120m, 130m, 110m, 125m)).ShouldBe(stopped);
    }

    private static IdeaTrack Run(IdeaPlan plan, params IdeaCandle[] minutes)
    {
        return minutes.Aggregate(IdeaTrack.Fired(plan, Fired, 5), IdeaTracker.Step);
    }

    private static IdeaCandle Minute(int index, decimal open, decimal high, decimal low, decimal close)
    {
        return new IdeaCandle(Fired.AddMinutes(index), open, high, low, close, 100m);
    }
}