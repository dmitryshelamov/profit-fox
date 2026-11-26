using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Commands.Runs.RunNext;

namespace ProfitFox.Tests.Unit.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     Change fix-backtest-random-control, design.md, decision 6, task 4.3: the progress of a backtest is half the sum of the
///     pairs passed in its two passes, rounded down.
/// </summary>
public sealed class PairHalvesTests
{
    [Test]
    public void After_the_first_pass_of_four_pairs_two_of_four_are_passed()
    {
        var progress = new RunProgress();
        progress.Begin(4);
        var halves = new PairHalves(progress);

        halves.Passed(1);
        Assert.That(progress.Done, Is.Zero, "half a pair is not a pair");
        halves.Passed(1);
        halves.Passed(1);
        halves.Passed(1);

        Assert.That((progress.Done, progress.Total), Is.EqualTo((2, 4)));
    }

    [Test]
    public void After_the_second_pass_every_pair_is_passed()
    {
        var progress = new RunProgress();
        progress.Begin(4);
        var halves = new PairHalves(progress);

        for (int pass = 0; pass < 8; pass++)
        {
            halves.Passed(1);
        }

        Assert.That(progress.Done, Is.EqualTo(4));
    }

    [Test]
    public void A_pair_of_a_bot_is_passed_whole()
    {
        var progress = new RunProgress();
        progress.Begin(3);
        var halves = new PairHalves(progress);

        halves.Passed(2);

        Assert.That(progress.Done, Is.EqualTo(1));
    }
}