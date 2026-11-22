using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     A reference strategy of <c>Fixtures</c> — H0001 or H0002 — as a backtest of one set on 4h over every pair of the
///     fake market-data service, the pairs in the order of the catalog, up to the end of exploration of the references,
///     2026-05-16, with what the backtest computes for each order kept.
/// </summary>
internal static class ReferenceBacktests
{
    public static readonly DateTimeOffset End = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    /// <summary>
    ///     The rows of the orders and the numbers of the set; with <paramref name="seed" />, the draw of random entries takes it
    ///     instead of the seed of the graph.
    /// </summary>
    public static async Task<(IReadOnlyList<EventRow> Rows, SetNumbers Numbers)> Run(MarketDataServer server, string reference, string? seed = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, $"{reference}.strategy.json")));
        GraphDefinition graph = StrategyGraph.ReadForm(document.RootElement).Value!;
        var settings = new BacktestSettings([], [TimeSpan.FromHours(4)], HistoryPeriod.DataStart, End, Pairs: null, 0.00055, 0.0002);
        BacktestPlan plan = BacktestPlans.Of(graph, StrategyKind.Idea, settings, BacktestGrid.Of(settings)).Single();
        if (seed is not null)
        {
            plan = plan with { Design = plan.Design with { Seed = seed } };
        }

        await using ServiceProvider services = BenchServices.Start(server);
        using var hash = new HistoryHash();
        var history = new MarketHistory(services.GetRequiredService<IHistorySource>(), End, hash);
        List<EventRow> rows = [];
        SetNumbers numbers = SetNumbers.Zero;
        foreach (Instrument pair in await services.GetRequiredService<IInstrumentCatalog>().LinearUsdtPerpetuals(CancellationToken.None))
        {
            PairSetResult result = (await PairBacktestRun.RunAsync(history, pair, StrategyKind.Idea, [plan], CancellationToken.None)).Single();
            rows.AddRange(result.Rows);
            numbers += result.Numbers;
        }

        return (rows, numbers);
    }
}
