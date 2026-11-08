using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Путь сигнала" and "Касания в одной минуте": signals on 15 minute candles fired at the end of the minute 09:59, their paths
///     stepped minute by minute. Times are minutes from the firing (the entry) to the end of the minute that touched.
/// </summary>
public class SignalPathTrackerTests
{
    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2024-03-01T10:00:00Z");
    private static readonly IdeaPlan Market = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m);

    // Scenario "Путь до стопа" on the reference set: +0.5 R at 10:29, +1 R at 11:00, the stop an hour later at 12:00; the closes at the ends of
    // the grid of 15 minutes before it: 1, 2, 3, 4, 6 and 8 candles.
    [Test]
    public void Path_ends_at_the_stop()
    {
        SignalPath path = Run(Market, IdeaFixtures.PathToStop());

        (path.Entry, path.EntryAfter, path.StopAfter, path.TrackedUntil, path.IsOpen).ShouldBe((100m, (int?)1, (int?)121, Fired.AddMinutes(121), false));
        path.LadderAfter.ShouldBe([30, 61, null, null, null, null, null, null, null, null]);
        path.Closes.ShouldBe([100.52m, 100.97m, 101.42m, 101.87m, 100.015m, 98.065m]);
    }

    [Test]
    public void Path_of_an_open_signal_goes_on_with_the_minutes()
    {
        SignalPath path = Run(Market, [.. IdeaFixtures.PathToStop().Take(61)]);

        (path.StopAfter, path.TrackedUntil, path.IsOpen).ShouldBe(((int?)null, Fired.AddMinutes(61), true));
        path.LadderAfter.ShouldBe([30, 61, null, null, null, null, null, null, null, null]);
    }

    // Scenario "Сигнал без входа": the limit of a pullback untouched for 7 days.
    [Test]
    public void Pullback_without_a_touch_for_seven_days_ends_without_an_entry()
    {
        var pullback = new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 99m, 97m, 103m);
        // The minute that opens 7 days after the firing touches the limit too late, and closes the longest window.
        IdeaCandle[] week = [.. Flat(0, 10080, 100m), Minute(10080, 100m, 100m, 98m, 99m)];

        SignalPath open = Run(pullback, week[..10080]);
        SignalPath path = Run(pullback, week);

        open.IsOpen.ShouldBeTrue();
        (path.Entry, path.EntryAfter, path.TrackedUntil, path.IsOpen).ShouldBe((99m, (int?)null, Fired.AddDays(7).AddMinutes(1), false));
    }

    // Scenario "Цель и стоп в одной минуте": the stop counts, +2 R does not.
    [Test]
    public void Minute_that_reaches_a_level_and_the_stop_touches_only_the_stop()
    {
        SignalPath path = Run(Market, Minute(0, 100m, 100.5m, 99.5m, 100m), Minute(1, 100m, 105m, 97m, 99m));

        path.StopAfter.ShouldBe(2);
        path.LadderAfter.ShouldAllBe(after => after == null);
    }

    // A path is 7 days from the entry at most.
    [Test]
    public void Path_ends_seven_days_after_the_entry()
    {
        IdeaCandle[] week = Flat(0, 10080, 100m);

        Run(Market, week[..10079]).IsOpen.ShouldBeTrue();
        SignalPath path = Run(Market, week);
        (path.IsOpen, path.Closes.Count).ShouldBe((false, ExitRuleGrid.Of(Interval.FifteenMinutes).Count));
    }

    // In the minute of a fill by a limit only the stop counts; a market entry takes its whole first minute.
    [Test]
    public void Pullback_touches_its_levels_from_the_minute_after_the_fill()
    {
        var pullback = new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 99m, 97m, 103m);

        SignalPath path = Run(pullback, Minute(0, 100m, 100m, 99.5m, 99.8m), Minute(1, 99.8m, 100.5m, 98.9m, 100.4m), Minute(2, 100.4m, 100.5m, 100m, 100.2m));

        (path.EntryAfter, path.LadderAfter[0]).ShouldBe(((int?)2, (int?)2));
        Run(pullback, Minute(0, 99.5m, 99.5m, 96m, 97m)).StopAfter.ShouldBe(1);
        Run(Market, Minute(0, 100m, 101.5m, 99.5m, 101m)).LadderAfter[0].ShouldBe(1);
    }

    // Requirement "Вход «Стоп-входа»": a gap fills at the open, and the levels go from it, on the price step towards the entry.
    [Test]
    public void Stop_entry_with_a_gap_measures_its_levels_from_the_open()
    {
        var stopEntry = new IdeaPlan(IdeaType.StopEntry, IdeaSide.Long, 102.01m, 99.5m, 107.03m);

        SignalPath path = Run(stopEntry, Minute(0, 101m, 101.5m, 100.5m, 101m), Minute(1, 102.30m, 102.6m, 102.2m, 102.5m), Minute(2, 102.5m, 103.71m, 102.4m, 103.6m));

        (path.Entry, path.EntryAfter, path.LadderAfter[0], path.LadderAfter[1]).ShouldBe((102.30m, (int?)2, (int?)2, (int?)null));
    }

    // Scenario "Пробой в другую сторону": the stop before the entry ends the path.
    [Test]
    public void Stop_entry_that_reaches_its_stop_before_the_entry_ends_without_one()
    {
        var stopEntry = new IdeaPlan(IdeaType.StopEntry, IdeaSide.Long, 102.01m, 99.5m, 107.03m);

        SignalPath path = Run(stopEntry, Minute(0, 101m, 101.5m, 100.5m, 101m), Minute(1, 101m, 101.2m, 99.4m, 99.6m));

        (path.EntryAfter, path.StopBeforeEntryAfter, path.IsOpen).ShouldBe(((int?)null, (int?)2, false));
    }

    // A market signal that would enter beyond its stop has no entry; «Уровни» beyond their target neither, and they track the zone ahead.
    [Test]
    public void Market_signal_beyond_its_stop_ends_without_an_entry()
    {
        var levels = new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 100m, 98m, 103m);

        Run(Market, Minute(0, 97.5m, 98m, 97m, 97.5m)).ShouldSatisfyAllConditions(path => path.EntryAfter.ShouldBeNull(), path => path.IsOpen.ShouldBeFalse());
        Run(levels, Minute(0, 103.5m, 104m, 103m, 103.5m)).EntryAfter.ShouldBeNull();
        Run(levels, Minute(0, 100m, 101m, 99.5m, 101m), Minute(1, 101m, 103m, 100.8m, 102.9m)).TargetAfter.ShouldBe(2);
    }

    [Test]
    public void Short_mirrors_the_levels()
    {
        var shortPlan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m);

        SignalPath path = Run(shortPlan, Minute(0, 100m, 100.5m, 98.9m, 99m), Minute(1, 99m, 102m, 98.5m, 101.5m));

        (path.LadderAfter[0], path.StopAfter).ShouldBe(((int?)1, (int?)2));
    }

    // Decision 1 of speed-up-ideas-replay: the replay steps every open path every minute, so a minute that touches nothing gives the path back as it
    // is, without moving TrackedUntil; the paths of the steps are the same once TrackedUntil is set.
    [Test]
    public void Lazy_step_gives_the_same_path_back_while_nothing_touches_it()
    {
        SignalPath entered = SignalPathTracker.Step(SignalPath.Fired(Market, Interval.FifteenMinutes, Fired, 0.01m), Minute(0, 100m, 100.4m, 99.8m, 100.2m));

        SignalPath lazy = SignalPathTracker.StepLazily(entered, Minute(1, 100.2m, 100.6m, 99.9m, 100.3m));

        ReferenceEquals(lazy, entered).ShouldBeTrue();
        lazy.TrackedUntil.ShouldBe(Fired.AddMinutes(1));
    }

    [Test]
    public void Lazy_steps_give_the_path_of_the_steps_once_tracked_until_is_set()
    {
        IdeaCandle[] minutes = [.. IdeaFixtures.PathToStop().Take(50)];

        SignalPath lazy = minutes.Aggregate(SignalPath.Fired(Market, Interval.FifteenMinutes, Fired, 0.01m), SignalPathTracker.StepLazily);

        // The close at the end of the third candle of the grid, 45 minutes after the entry, is the last touch.
        lazy.TrackedUntil.ShouldBe(Fired.AddMinutes(45));
        Same(lazy with { TrackedUntil = Fired.AddMinutes(50) }, Run(Market, minutes));
        Same(IdeaFixtures.PathToStop().Aggregate(SignalPath.Fired(Market, Interval.FifteenMinutes, Fired, 0.01m), SignalPathTracker.StepLazily), Run(Market, IdeaFixtures.PathToStop()));
    }

    // A path that ends by time (a market signal without an entry, the horizon of a pullback) ends on the lazy step of the minute after, tracked until
    // the open of that minute: the same path as the steps give.
    [Test]
    public void Lazy_step_ends_a_path_by_time_on_the_minute_after()
    {
        IdeaCandle beyond = Minute(0, 97.5m, 98m, 97m, 97.5m);
        var pullback = new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 99m, 97m, 103m);
        IdeaCandle[] week = [.. Flat(0, 10080, 100m), Minute(10080, 100m, 100m, 98m, 99m)];

        SignalPath stale = SignalPathTracker.StepLazily(SignalPath.Fired(Market, Interval.FifteenMinutes, Fired, 0.01m), beyond);
        SignalPath ended = SignalPathTracker.StepLazily(stale, Minute(1, 97.5m, 98m, 97m, 97.5m));
        SignalPath lateByAWeek = ((IEnumerable<IdeaCandle>)[.. week, Minute(10081, 99m, 99m, 99m, 99m)]).Aggregate(SignalPath.Fired(pullback, Interval.FifteenMinutes, Fired, 0.01m), SignalPathTracker.StepLazily);

        stale.IsOpen.ShouldBeTrue();
        Same(ended, Run(Market, beyond));
        ended.IsOpen.ShouldBeFalse();
        Same(lateByAWeek, Run(pullback, week));
    }

    // Two paths with the same values: the ladder and the closes are lists, which a record compares by reference.
    private static void Same(SignalPath actual, SignalPath expected)
    {
        actual.LadderAfter.ShouldBe(expected.LadderAfter);
        actual.Closes.ShouldBe(expected.Closes);
        (actual with { LadderAfter = expected.LadderAfter, Closes = expected.Closes }).ShouldBe(expected);
    }

    private static SignalPath Run(IdeaPlan plan, params IdeaCandle[] minutes)
    {
        return minutes.Aggregate(SignalPath.Fired(plan, Interval.FifteenMinutes, Fired, 0.01m), SignalPathTracker.Step);
    }

    private static SignalPath Run(IdeaPlan plan, IReadOnlyList<IdeaCandle> minutes)
    {
        return Run(plan, [.. minutes]);
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