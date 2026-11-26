using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     A reference strategy of <c>Fixtures</c> — H0001 or H0002 — as a backtest of one set on 4h over every pair of the
///     fake market-data service in the order of the catalog, in the two passes of a backtest, up to the end of exploration
///     of the references, 2026-05-16, with what the backtest computes for each order and each random entry kept. The current
///     day is that of the clock of the fake service.
/// </summary>
internal static class ReferenceBacktests
{
    public static readonly DateTimeOffset End = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    /// <summary>
    ///     The rows of the orders, the random entries and the numbers of the set with its cells of control, in the order of
    ///     the pairs.
    /// </summary>
    public static async Task<ReferenceRun> Run(MarketDataServer server, string reference)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, $"{reference}.strategy.json")));
        GraphDefinition graph = StrategyGraph.ReadForm(document.RootElement).Value!;
        var settings = new BacktestSettings([], [TimeSpan.FromHours(4)], HistoryPeriod.DataStart, End, Pairs: null, 0.00055, 0.0002);
        BacktestPlan plan = BacktestPlans.Of(graph, StrategyKind.Idea, settings, BacktestGrid.Of(settings)).Single();

        await using ServiceProvider services = BenchServices.Start(server);
        using var hash = new HistoryHash();
        var history = new MarketHistory(services.GetRequiredService<IHistorySource>(), End, hash, server.Data.Time);
        List<EventRow> rows = [];
        List<RandomTrade> randoms = [];
        IReadOnlyList<Instrument> pairs = await services.GetRequiredService<IInstrumentCatalog>().LinearUsdtPerpetuals(CancellationToken.None);
        SetTotals set = (await PairBacktestRun.RunAsync(
            history,
            pairs,
            StrategyKind.Idea,
            [plan],
            CancellationToken.None,
            computed: (_, results) =>
            {
                rows.AddRange(results[0].Rows);
                randoms.AddRange(results[0].Randoms);
            })).Single();

        return new ReferenceRun(rows, randoms, set.Numbers, set.Cells!);
    }
}

/// <summary>
///     What a reference strategy gave: the rows of its orders and its random entries in the order of the pairs, the numbers
///     of its set and its cells of control.
/// </summary>
internal sealed record ReferenceRun(IReadOnlyList<EventRow> Rows, IReadOnlyList<RandomTrade> Randoms, SetNumbers Numbers, ControlCells Cells)
{
    /// <summary>
    ///     The rows of the orders and the numbers of the set, as the tests of the previews read a reference.
    /// </summary>
    public void Deconstruct(out IReadOnlyList<EventRow> rows, out SetNumbers numbers)
    {
        rows = Rows;
        numbers = Numbers;
    }
}