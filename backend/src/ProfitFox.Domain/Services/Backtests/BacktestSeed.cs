using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The seed of the draw of random entries of a set (change add-strategy-backtest, design.md, decision 4): the SHA-256
///     of the canonical JSON of the expanded graph of the set and of its timeframe. It depends only on what the set computes,
///     so a set draws the same entries alone and among other sets, and a number written with trailing zeros, such as
///     <c>0.00050</c>, is the same number as without them (task 12.4).
/// </summary>
public static class BacktestSeed
{
    public static string Of(GraphDefinition graph, TimeSpan timeframe)
    {
        string text = $"{StrategyGraph.Write(Canonical(graph))}\n{Timeframes.Name(timeframe)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    // The graph with every number at its smallest scale; a graph whose numbers have no trailing zeros is written as it was.
    private static GraphDefinition Canonical(GraphDefinition graph)
    {
        return graph with
        {
            Parameters =
            [
                .. graph.Parameters.Select(parameter => parameter with
                {
                    Value = Canonical(parameter.Value),
                    Minimum = parameter.Minimum is { } minimum ? Canonical(minimum) : null,
                    Maximum = parameter.Maximum is { } maximum ? Canonical(maximum) : null
                })
            ],
            Nodes =
            [
                .. graph.Nodes.Select(node => node with
                {
                    Inputs = node.Inputs.ToDictionary(input => input.Key, input => Canonical(input.Value), StringComparer.Ordinal)
                })
            ]
        };
    }

    private static GraphValue Canonical(GraphValue value)
    {
        return value.Type == PinType.Number ? GraphValue.FromNumber(Canonical(value.Number)) : value;
    }

    // Dividing by one with the largest scale drops the trailing zeros of a decimal.
    private static decimal Canonical(decimal number)
    {
        return number / 1.0000000000000000000000000000m;
    }
}
