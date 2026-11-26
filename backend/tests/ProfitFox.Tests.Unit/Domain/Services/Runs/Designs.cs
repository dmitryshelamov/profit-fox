using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Runs;

/// <summary>
///     The strategy of H0001 and the history of exploration for the tests: from the start of the data to 2026-05-16, the
///     end of exploration before change add-strategy-backtest, on 4h.
/// </summary>
internal static class Designs
{
    public static readonly DateTimeOffset ExplorationEnd = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     The strategy of H0001 as its test file keeps it.
    /// </summary>
    public static readonly TradingStrategy H0001 = Read(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "H0001.strategy.json")));

    public static BacktestDesign Exploration(int randoms = 20)
    {
        return new BacktestDesign(HistoryPeriod.DataStart, ExplorationEnd, TimeSpan.FromHours(4), TradeCosts.Default) { Randoms = randoms };
    }

    /// <summary>
    ///     The strategy of H0001 with the time <paramref name="after" /> and, when given, a stop and a target.
    /// </summary>
    public static TradingStrategy Strategy(string after = "4h", decimal? stop = null, decimal? target = null)
    {
        return Read(Graphs.ChangedStrategy(graph =>
        {
            graph.Node("close-after")["inputs"] = new JsonObject { ["after"] = after };
            if (stop is { } distance)
            {
                graph.AddExit("stop-loss", "stop-loss", "distance", JsonValue.Create(distance));
            }

            if (target is { } reach)
            {
                graph.AddExit("take-profit", "take-profit", "distance", JsonValue.Create(reach));
            }
        }));
    }

    public static DateTimeOffset Utc(int year, int month, int day, int hour = 0)
    {
        return new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero);
    }

    private static TradingStrategy Read(string json)
    {
        ReadResult<TradingStrategy> read = StrategyGraph.ReadStrategy(json, StrategyKind.Idea);
        Assert.That(read.Errors, Is.Empty);
        return read.Value!;
    }
}