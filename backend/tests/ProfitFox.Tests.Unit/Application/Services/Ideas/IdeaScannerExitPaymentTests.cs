using Microsoft.Extensions.Logging;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 27.2: a funding payment at the moment of the exit of an idea. A backtest counts the
///     payments at <c>entry &lt; t ≤ exit</c>, so a cycle that exits at the time of a payment pays it — as <c>SOXSUSDT</c> on
///     5m 2026-11-23: the idea of 07:50 stopped at 08:00:00 had −0.01158923, its cycle −0.01087417, the difference the payment
///     of 08:00. The data service gives a payment 59 to 177 s after its time, and an idea closed 90 s after its exit
///     (<c>Scanner:ExitWait</c>). The pair <c>BTCUSDT</c> pays every four hours, at 3:00 and 7:00 at the rate 0.0001, and at
///     11:00 at −0.001: <c>close-up</c> enters long at 10:50 with a stop of 1 %, reached in the minute 11:00.
/// </summary>
public sealed class IdeaScannerExitPaymentTests
{
    private static readonly DateTimeOffset Eleven = Ten.AddHours(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The idea that exits at the time of a payment of its pair waits for that payment and counts it, as the cycle of a
    ///     backtest does, whether it comes before the usual wait of 90 s is over or after it.
    /// </summary>
    [Test]
    public async Task An_idea_that_exits_at_the_time_of_a_payment_waits_for_it_and_ends_as_the_cycle_of_a_backtest([Values(30, 170)] int seconds)
    {
        ScannerRig rig = await StoppedAtEleven();

        for (int second = 16; second < 600; second += 30)
        {
            if (second >= seconds && !rig.Paid.Any(paid => paid.Time == Eleven))
            {
                await rig.Pay("BTCUSDT", Eleven, -0.001m);
            }

            await rig.At(Eleven.AddSeconds(second));
            if (second < seconds)
            {
                Assert.That(rig.Ideas.Ideas.Single().State, Is.EqualTo(IdeaState.Open), "the payment of 11:00 has not come");
            }
        }

        Idea idea = rig.Ideas.Ideas.Single();
        (DateTimeOffset moment, CycleOutcome cycle) = rig.Backtest("close-up", FiveMinutes, "BTCUSDT", Eleven.AddHours(1)).Single();
        Assert.That((idea.State, idea.Moment, idea.ExitTime, idea.ExitReason), Is.EqualTo((IdeaState.Closed, moment, (DateTimeOffset?)cycle.Fills[^1].Moment, ExitReason.Stop)));
        Assert.That(cycle.Funding, Is.Not.Zero, "the cycle pays the payment of 11:00");
        Assert.That(idea.Result, Is.EqualTo(cycle.Net).Within(1e-9), "the result of the cycle of a backtest");
        Assert.That(rig.Log.Entries.Where(entry => entry.Level >= LogLevel.Warning), Is.Empty);
    }

    /// <summary>
    ///     A payment that never comes does not hold the idea for ever: ten minutes after its exit it is closed without the
    ///     payment, and the journal names the pair and the time of the payment.
    /// </summary>
    [Test]
    public async Task An_idea_whose_payment_does_not_come_is_closed_without_it_ten_minutes_after_its_exit()
    {
        ScannerRig rig = await StoppedAtEleven();

        await rig.At(Eleven.AddMinutes(9).AddSeconds(59));
        Assert.That(rig.Ideas.Ideas.Single().State, Is.EqualTo(IdeaState.Open));
        Assert.That(rig.Log.Entries, Is.Empty);

        await rig.At(Eleven.AddMinutes(10));
        Idea idea = rig.Ideas.Ideas.Single();
        Assert.That((idea.State, idea.FundingPaid), Is.EqualTo((IdeaState.Closed, 0d)));
        (LogLevel Level, string Message) warning = rig.Log.Entries.Single();
        Assert.That(warning.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(warning.Message, Does.Contain("BTCUSDT").And.Contain("2026-11-22T11:00:00"));
    }

    /// <summary>
    ///     An idea whose exit is not the time of a payment closes 90 s after it, as before: nothing is waited for.
    /// </summary>
    [Test]
    public async Task An_idea_that_exits_between_the_payments_closes_after_the_usual_wait()
    {
        ScannerRig rig = await StoppedAtEleven(stopMinute: 1);

        await rig.At(Eleven.AddMinutes(1).AddSeconds(89));
        Assert.That(rig.Ideas.Ideas.Single().State, Is.EqualTo(IdeaState.Open));
        await rig.At(Eleven.AddMinutes(1).AddSeconds(90));
        Assert.That(rig.Ideas.Ideas.Single().State, Is.EqualTo(IdeaState.Closed));
    }

    // The scanner at 10:00:05 with close-up on 5m for an hour with a stop of 1 %, the payments of 3:00 and 7:00 in the book;
    // the minutes of BTCUSDT from 10:00 live: closes of 100 to 10:44 and of 101 from 10:45 — the idea of 10:50 — then of
    // 100.5, and the minute 11:00 (or the one given) goes down to 98: the stop. The minutes go on at 100 to 11:10.
    private static async Task<ScannerRig> StoppedAtEleven(int stopMinute = 0)
    {
        var rig = new ScannerRig();
        rig.History.Payments["btc"] = [new HistoryPayment(Ten.AddHours(-7), "0.0001"), new HistoryPayment(Ten.AddHours(-3), "0.0001")];
        await rig.Enable("close-up", "1h", stop: 0.01m, timeframes: [FiveMinutes]);
        await rig.Start();
        await rig.Pay("BTCUSDT", Ten.AddHours(-7), 0.0001m);
        await rig.Pay("BTCUSDT", Ten.AddHours(-3), 0.0001m);
        for (int minute = 0; minute < 70; minute++)
        {
            DateTimeOffset open = Ten.AddMinutes(minute);
            if (minute == 60 + stopMinute)
            {
                await rig.Minutes("BTCUSDT", open, 1, close: 99, low: 98);
            }
            else
            {
                await rig.Minutes("BTCUSDT", open, 1, close: minute < 45 ? 100 : minute < 50 ? 101 : minute < 60 + stopMinute ? 100.5m : 100);
            }

            if (minute < 60 + stopMinute)
            {
                await rig.At(open.AddMinutes(1).AddSeconds(16));
            }
        }

        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Moment), Is.EqualTo(new[] { Ten.AddMinutes(50) }));
        return rig;
    }
}
