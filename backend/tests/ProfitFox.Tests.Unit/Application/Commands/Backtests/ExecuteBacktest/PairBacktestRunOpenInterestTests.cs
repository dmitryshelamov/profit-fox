using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Unit.Application.Queries.MarketData;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     Change add-idea-score, task 5.3: spec market-history «Граф без узла OI» and «Граф с узлом OI» — a backtest reads the
///     points of open interest of a pair only for a graph with the node «OI», and the points it reads go into the hash.
/// </summary>
public sealed class PairBacktestRunOpenInterestTests
{
    private static readonly DateTimeOffset End = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly Instrument Btc = new Instrument("btc", "BTCUSDT");

    [Test]
    public async Task A_graph_without_the_node_reads_no_open_interest()
    {
        var source = new FakeHistorySource();

        await Run(source, Graphs.H0001Strategy);

        Assert.That(source.Reads.Select(read => read.Feed), Has.None.EqualTo("open-interest"));
        Assert.That(source.Reads.Select(read => read.Feed), Does.Contain("funding"));
    }

    [Test]
    public async Task A_graph_with_the_node_reads_the_open_interest_of_the_period_into_the_hash()
    {
        var withNode = new FakeHistorySource();
        var without = new FakeHistorySource();
        var again = new FakeHistorySource();

        string first = await Run(withNode, OpenInterestStrategy);
        string plain = await Run(without, Graphs.H0001Strategy);
        string second = await Run(again, OpenInterestStrategy);

        Assert.That(withNode.Reads.Where(read => read.Feed == "open-interest"), Is.EqualTo(new[] { ("open-interest", HistoryPeriod.DataStart, End.AddSeconds(1)) }));
        Assert.That(first, Is.Not.EqualTo(plain), "the points are in the hash");
        Assert.That(second, Is.EqualTo(first), "the same read gives the same hash");
    }

    // The strategy of H0001 on the open interest in place of the rate of funding.
    private static readonly string OpenInterestStrategy = Graphs.H0001Strategy.Replace("\"funding-rate\"", "\"open-interest\"", StringComparison.Ordinal);

    private static async Task<string> Run(FakeHistorySource source, string json)
    {
        using var document = JsonDocument.Parse(json);
        GraphDefinition graph = StrategyGraph.ReadForm(document.RootElement).Value!;
        var settings = new BacktestSettings([], [TimeSpan.FromHours(4)], HistoryPeriod.DataStart, End, Pairs: null, 0.00055, 0.0002);
        BacktestPlan plan = BacktestPlans.Of(graph, StrategyKind.Idea, settings, BacktestGrid.Of(settings)).Single();
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, End, hash, new FakeTimeProvider(End.AddMonths(3)));

        await PairBacktestRun.RunAsync(history, Btc, StrategyKind.Idea, [plan], CancellationToken.None);

        return hash.Result();
    }
}
