using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The seed of the draw of random entries of a set (change add-strategy-backtest, design.md, decision 4): the SHA-256
///     of the canonical JSON of the expanded graph of the set and of its timeframe. It depends only on what the set computes,
///     so a set draws the same entries alone and among other sets.
/// </summary>
public static class BacktestSeed
{
    public static string Of(GraphDefinition graph, TimeSpan timeframe)
    {
        string text = $"{StrategyGraph.Write(graph)}\n{Timeframes.Name(timeframe)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
