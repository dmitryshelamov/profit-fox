using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>Candles loaded of those expected while loading, combinations calculated of all while running.</summary>
public sealed record ProgressResponse(long LoadedCandles, long ExpectedCandles, long CalculatedCombinations, long TotalCombinations)
{
    public static ProgressResponse Of(Backtest backtest)
    {
        return new ProgressResponse(backtest.LoadedCandles, backtest.ExpectedCandles, backtest.CalculatedCombinations, backtest.TotalCombinations);
    }
}