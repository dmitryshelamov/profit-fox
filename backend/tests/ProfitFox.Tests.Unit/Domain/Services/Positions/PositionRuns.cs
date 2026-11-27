using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Positions;

/// <summary>
///     Runs of strategies with orders for the tests: hourly candles closing at 12:00, 13:00 and on from
///     <see cref="Paths.Start" />, the minutes of a <see cref="Paths" /> from 12:00.
/// </summary>
internal static class PositionRuns
{
    public static readonly TradeCosts Free = new TradeCosts(TakerFee: 0, Slippage: 0, Funding: false, MakerFee: 0);

    /// <summary>
    ///     The hourly candle closing <paramref name="hour" /> hours after 12:00, every price <paramref name="close" />.
    /// </summary>
    public static ClosedCandle Hour(int hour, decimal close = 100)
    {
        DateTimeOffset closeTime = Paths.Start + TimeSpan.FromHours(hour);
        return new ClosedCandle(closeTime - TimeSpan.FromHours(1), closeTime, close, close, close, close, 1);
    }

    public static TradingStrategy Strategy(string json)
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json);
        Assert.That(result.Errors, Is.Empty, "the test strategy must pass the checks");
        return result.Value!;
    }

    /// <summary>
    ///     The strategy on the path, the candles closing at 12:00 and every hour after it up to <paramref name="hours" />,
    ///     finished at the end of the last hour, which is the end of the computed history.
    /// </summary>
    public static PositionResult Run(string json, MinutePath path, int hours, TradeCosts? costs = null, Func<int, decimal>? close = null)
    {
        DateTimeOffset end = Paths.Start + TimeSpan.FromHours(hours + 1);
        var run = new PositionRun(Strategy(json), path, costs ?? Free, Paths.Start, end);
        for (int hour = 0; hour <= hours; hour++)
        {
            run.Step(Hour(hour, close?.Invoke(hour) ?? 100), facts: null);
        }

        return run.Finish(end);
    }

    /// <summary>
    ///     A strategy of one node <c>position</c> and the given nodes and wires, with parameters.
    /// </summary>
    public static string WithPosition(string nodes, string wires, string parameters = "")
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "parameters": [{{parameters}}],
                   "nodes": [{ "id": "position", "kind": "position" }, {{nodes}}],
                   "wires": [{{wires}}]
                 }
                 """;
    }

    /// <summary>
    ///     A node of an order wired into the position.
    /// </summary>
    public static string Into(string node)
    {
        return $$"""{ "from": "{{node}}.order", "to": "position.orders" }""";
    }
}
