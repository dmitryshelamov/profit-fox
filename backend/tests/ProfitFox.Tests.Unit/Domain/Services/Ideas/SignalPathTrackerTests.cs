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
        IdeaCandle[] week = Flat(0, 10080, 100m);

        SignalPath open = Run(pullback, week[..10079]);
        SignalPath path = Run(pullback, week);

        open.IsOpen.ShouldBeTrue();
        (path.Entry, path.EntryAfter, path.TrackedUntil, path.IsOpen).ShouldBe((99m, (int?)null, Fired.AddDays(7), false));
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