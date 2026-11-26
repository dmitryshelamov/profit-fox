using ProfitFox.Application.Commands.Runs.RunNext;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     The progress of a backtest by halves of pairs (change fix-backtest-random-control, design.md, decision 6): a pair of
///     a category of ideas counts as half passed after each of its two passes, a pair of a bot as passed after its one pass.
///     The pairs passed are half the sum of the halves, rounded down; how many pairs there are is the number of pairs of the
///     backtest.
/// </summary>
internal sealed class PairHalves(RunProgress? progress)
{
    private int _halves;
    private int _advanced;

    public void Passed(int halves)
    {
        _halves += halves;
        for (; _advanced < _halves / 2; _advanced++)
        {
            progress?.Advance();
        }
    }
}