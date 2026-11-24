using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 27.1: the order of the close <c>C</c> in whose first minute the previous idea exits.
///     The exit of a stop or a target is the open of the minute where it fired, so an exit in <c>[C, C+1)</c> is written as
///     <c>C</c>, and a backtest of a strategy by plan takes the order of <c>C</c> when the previous cycle exited at <c>C</c> or
///     earlier (<c>PairPosition.Enter</c>: an order before the exit is skipped). Live the minute <c>C</c> is not known at the
///     close of <c>C</c>; the scanner holds the order until it is, and gives the idea of <c>C</c> with the moment and the price
///     of that close when the previous one exited in it, as <c>BTCUSDT</c> on 5m 2026-11-23: the cycle of 14:15 closed by its
///     target at 14:25, the next one entered at 14:25 and reached its target at 14:26. <c>close-up</c> enters long for an
///     hour the first time a candle of 5m closes above 100, with a target of 1 %: the signal at 10:15 and at 10:25.
/// </summary>
public sealed class IdeaScannerExitCloseTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset End = Ten.AddMinutes(90);

    /// <summary>
    ///     The idea of 10:15 reaches its target in the minute 10:25: the backtest takes the order of 10:25, and the scanner gives
    ///     its idea, live as in a catch-up, in one pass or two. Before the fix the live scanner gave none — the idea of 10:15
    ///     was open at the close of 10:25 — and a catch-up in one pass none either.
    /// </summary>
    [Test]
    public async Task An_exit_in_the_first_minute_of_the_close_lets_the_order_of_that_close_in_as_a_backtest([Values] Mode mode)
    {
        (Idea[] ideas, IReadOnlyList<(DateTimeOffset Moment, CycleOutcome Cycle)> backtest) = await Run(mode, exitInTheFirstMinute: true);

        Assert.That(backtest.Select(item => item.Moment), Is.EqualTo(new[] { Ten.AddMinutes(15), Ten.AddMinutes(25) }), "the backtest takes the order of 10:25");
        Assert.That(ideas.First().ExitTime, Is.EqualTo(Ten.AddMinutes(25)));
        Assert.That(ideas[1].CandleClose, Is.EqualTo(100.5m), "the price of the close of 10:25");
        AssertTheTrades(ideas, backtest);
    }

    /// <summary>
    ///     The idea of 10:15 reaches its target in the minute 10:26, after the close of 10:25: the backtest skips the order of
    ///     10:25, and the scanner gives no idea of it, live as in a catch-up.
    /// </summary>
    [Test]
    public async Task An_exit_after_the_first_minute_of_the_close_keeps_the_order_of_that_close_out_as_a_backtest([Values] Mode mode)
    {
        (Idea[] ideas, IReadOnlyList<(DateTimeOffset Moment, CycleOutcome Cycle)> backtest) = await Run(mode, exitInTheFirstMinute: false);

        Assert.That(backtest.Select(item => item.Moment), Is.EqualTo(new[] { Ten.AddMinutes(15) }), "the backtest skips the order of 10:25");
        Assert.That(ideas.Single().ExitTime, Is.EqualTo(Ten.AddMinutes(26)));
        AssertTheTrades(ideas, backtest);
    }

    public enum Mode
    {
        Live,
        CatchUpInTwoPasses,
        CatchUpInOnePass
    }

    private static void AssertTheTrades(Idea[] ideas, IReadOnlyList<(DateTimeOffset Moment, CycleOutcome Cycle)> backtest)
    {
        Assert.That(ideas.Select(idea => idea.State), Has.All.EqualTo(IdeaState.Closed));
        Assert.That(
            ideas.Select(idea => (idea.Moment, idea.EntryTime, idea.ExitTime, idea.ExitReason)),
            Is.EqualTo(backtest.Select(item => (item.Moment, (DateTimeOffset?)item.Cycle.Fills[0].Moment, (DateTimeOffset?)item.Cycle.Fills[^1].Moment, Reason(item.Cycle.ExitReason)))));
        Assert.That(ideas.Select(idea => idea.Result!.Value), Is.EqualTo(backtest.Select(item => item.Cycle.Net!.Value)).Within(1e-9), "the results of the cycles");
    }

    private static ExitReason Reason(CycleExit exit)
    {
        return exit switch
        {
            CycleExit.Stop => ExitReason.Stop,
            CycleExit.Target => ExitReason.Target,
            _ => ExitReason.Time
        };
    }

    // The minutes of BTCUSDT from 10:00: closes of 100, then 101 from 10:10 — the signal at 10:15, where the idea enters at
    // 100 with its target at about 101.05; closes of 100 from 10:15 and of 100.5 from 10:20 — the signal again at 10:25. The
    // minute 10:25 opens at 100.5 and reaches 101.3 (the target of 10:15 in it) or only 100.9 (the target in the minute 10:26,
    // which reaches 101.3); the minute 10:26 reaches 101.7, the target of an idea that entered at 100.5. Then closes of 101 to
    // 11:30. Live: a pass 16 s after each minute; a catch-up at 11:45: in two passes, one after the minute 10:15 and one after
    // the last; in one pass, after the last.
    private static async Task<(Idea[] Ideas, IReadOnlyList<(DateTimeOffset Moment, CycleOutcome Cycle)> Backtest)> Run(Mode mode, bool exitInTheFirstMinute)
    {
        var rig = new ScannerRig();
        await rig.Enable("close-up", "1h", target: 0.01m, timeframes: [FiveMinutes]);
        await rig.Start();
        DateTimeOffset now = End.AddMinutes(15);
        if (mode != Mode.Live)
        {
            rig.Time.SetUtcNow(now);
        }

        async Task Minute(int minute, decimal close, decimal? high = null, decimal open = 100)
        {
            await rig.Minutes("BTCUSDT", Ten.AddMinutes(minute), 1, close, high: high, open: open);
            if (mode == Mode.Live)
            {
                await rig.At(Ten.AddMinutes(minute + 1).AddSeconds(16));
            }
            else if (mode == Mode.CatchUpInTwoPasses && minute == 15)
            {
                await rig.At(now.AddSeconds(1), quiet: false);
            }
        }

        for (int minute = 0; minute < 10; minute++)
        {
            await Minute(minute, 100);
        }

        for (int minute = 10; minute < 15; minute++)
        {
            await Minute(minute, 101);
        }

        for (int minute = 15; minute < 20; minute++)
        {
            await Minute(minute, 100);
        }

        for (int minute = 20; minute < 25; minute++)
        {
            await Minute(minute, 100.5m);
        }

        await Minute(25, exitInTheFirstMinute ? 101 : 100.8m, high: exitInTheFirstMinute ? 101.3m : 100.9m, open: 100.5m);
        await Minute(26, 101, high: exitInTheFirstMinute ? 101.7m : 101.3m, open: exitInTheFirstMinute ? 101 : 100.8m);
        for (int minute = 27; minute <= 90; minute++)
        {
            await Minute(minute, 101);
        }

        await rig.At((mode == Mode.Live ? End.AddMinutes(1) : now).AddSeconds(30), quiet: mode == Mode.Live);
        await rig.At((mode == Mode.Live ? End.AddMinutes(1) : now).AddSeconds(31), quiet: mode == Mode.Live);
        return ([.. rig.Ideas.Ideas.OrderBy(idea => idea.Moment)], rig.Backtest("close-up", FiveMinutes, "BTCUSDT", End));
    }
}
