using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>
///     Requirement "Дневной стоп" on the reference trades of 7 November 2026: six trades of the autos of −5.40, −4.43, −4.02, −5.20, −4.36 and
///     −3.53 that closed at 09:10, 10:05, 11:30, 12:40, 13:55 and 14:20, and a manual one of −3.21 at 15:12. The running sum is −5.40, −9.83,
///     −13.85, −19.05, −23.41, −26.94 and −30.15.
/// </summary>
public class DailyLossStopTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Day.AddHours(16).AddMinutes(30);

    private static readonly (DateTimeOffset ClosedAt, decimal Result)[] Trades =
    [
        (Day.AddHours(9).AddMinutes(10), -5.40m),
        (Day.AddHours(10).AddMinutes(5), -4.43m),
        (Day.AddHours(11).AddMinutes(30), -4.02m),
        (Day.AddHours(12).AddMinutes(40), -5.20m),
        (Day.AddHours(13).AddMinutes(55), -4.36m),
        (Day.AddHours(14).AddMinutes(20), -3.53m),
        (Day.AddHours(15).AddMinutes(12), -3.21m)
    ];

    // Scenario "Сработал" up to the manual trade: −19.05 of 30 holds nothing.
    [Test]
    public void Sum_under_the_limit_does_not_hold()
    {
        DailyLossState state = DailyLossStop.Of(Trades.Take(4), Now, 30m);

        (state.Sum, state.TriggeredAt, state.ResumesAt, state.Holds).ShouldBe((-19.05m, null, null, false));
    }

    // Scenario "Сработал": −30.15 and 15:12 UTC, and the autos enter again at 00:00 UTC of the next day.
    [Test]
    public void Sum_that_reached_the_limit_holds_from_the_trade_that_reached_it()
    {
        DailyLossState state = DailyLossStop.Of(Trades, Now, 30m);

        (state.Sum, state.TriggeredAt, state.ResumesAt, state.Holds).ShouldBe((-30.15m, Day.AddHours(15).AddMinutes(12), Day.AddDays(1), true));
    }

    [Test]
    public void Sum_equal_to_the_limit_holds()
    {
        DailyLossState state = DailyLossStop.Of([(Day.AddHours(9), -30m)], Now, 30m);

        state.TriggeredAt.ShouldBe(Day.AddHours(9));
    }

    // Scenario "Плюс после срабатывания": the sum rose to −24.15 and the stop holds to the end of the day.
    [Test]
    public void Gain_after_the_trigger_does_not_release_the_stop()
    {
        DailyLossState state = DailyLossStop.Of([.. Trades, (Day.AddHours(16), 6.00m)], Now, 30m);

        (state.Sum, state.TriggeredAt).ShouldBe((-24.15m, Day.AddHours(15).AddMinutes(12)));
    }

    [Test]
    public void Gain_before_the_trigger_postpones_it()
    {
        DailyLossState state = DailyLossStop.Of([(Day.AddHours(8), 10m), .. Trades], Now, 30m);

        (state.Sum, state.TriggeredAt).ShouldBe((-20.15m, null));
    }

    // The trades come in any order: the sum runs by the time of the close.
    [Test]
    public void Trades_are_summed_by_the_time_they_closed()
    {
        DailyLossState state = DailyLossStop.Of(Trades.Reverse(), Now, 30m);

        state.TriggeredAt.ShouldBe(Day.AddHours(15).AddMinutes(12));
    }

    // Scenario "Учебные и вчерашние не считаются": the paper ones are not given at all, the trade of yesterday is left out here.
    [Test]
    public void Trade_of_the_day_before_is_not_counted()
    {
        DailyLossState state = DailyLossStop.Of([(Day.AddMinutes(-1), -40m)], Now, 30m);

        (state.Sum, state.TriggeredAt).ShouldBe((0m, null));
    }

    [Test]
    public void Trade_of_the_first_moment_of_the_day_is_counted()
    {
        DailyLossStop.Of([(Day, -7m)], Now, 30m).Sum.ShouldBe(-7m);
    }

    [Test]
    public void Day_is_the_day_of_now_in_utc()
    {
        // 23:30 at -03:00 is 02:30 of the next day in UTC.
        var late = new DateTimeOffset(2026, 11, 7, 23, 30, 0, TimeSpan.FromHours(-3));

        DailyLossStop.Of([(Day.AddHours(23), -7m)], late, 30m).Sum.ShouldBe(0m);
    }

    // Scenario "Выключить защиту": no limit, no stop.
    [Test]
    public void Without_a_limit_there_is_no_stop()
    {
        DailyLossState state = DailyLossStop.Of(Trades, Now, null);

        (state.Sum, state.TriggeredAt, state.ResumesAt).ShouldBe((-30.15m, null, null));
    }

    // A limit changed during the day recounts the trigger at once: 20 was first reached by the trade of 13:55.
    [Test]
    public void Lower_limit_triggers_at_the_trade_that_reached_it()
    {
        DailyLossStop.Of(Trades, Now, 20m).TriggeredAt.ShouldBe(Day.AddHours(13).AddMinutes(55));
    }
}