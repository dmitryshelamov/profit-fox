using System.Text.Json;

using Dapper;

using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices;

/// <summary>
///     Change add-backtest-history-cache, task 4.4: spec market-history-cache «Прогрев сканера» — the scanner of a Worker with
///     a folder of the cache warms its pairs up from the REST of the market-data service, as before, and puts nothing in the
///     folder.
/// </summary>
[NonParallelizable]
public sealed class HistoryCacheWarmupTests
{
    [SetUp]
    public async Task CreateBench()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task The_warm_up_of_the_scanner_leaves_the_folder_of_the_cache_empty()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        using var folder = new HistoryCacheFolder();
        await Enable();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var time = new FakeTimeProvider(new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.FromMinutes(5).Ticks), TimeSpan.Zero).AddMinutes(-30).AddSeconds(5));

        await using (await WorkerHost.Start(server, "w-1", scanner: true, time: time, cache: folder.Path))
        {
            await Poll.Until("the warm-up", async () =>
            {
                time.Advance(TimeSpan.FromMilliseconds(100));
                await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
                return await connection.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT warmed FROM scanner_lease), 0)") == 3;
            });
        }

        Assert.That(server.Data.RestRequests, Has.Some.StartsWith("candles"), "the warm-up reads the REST of the service");
        Assert.That(server.Data.CandleRequests, Is.Empty, "and not the History streams");
        Assert.That(Directory.Exists(folder.Path) ? Directory.GetFileSystemEntries(folder.Path, "*", SearchOption.AllDirectories) : [], Is.Empty);
    }

    // An idea strategy on 5m: long the first time the close is above 100, for 15 minutes.
    private static async Task Enable()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "nodes": [
                                { "id": "c", "kind": "candle" },
                                { "id": "up", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
                                { "id": "first", "kind": "first-time" },
                                { "id": "enter", "kind": "enter", "settings": { "side": "long" }, "inputs": { "size": 100 } },
                                { "id": "position", "kind": "position" },
                                { "id": "close-after", "kind": "close-after", "inputs": { "after": "15m" } }
                              ],
                              "wires": [
                                { "from": "c.close", "to": "up.a" },
                                { "from": "up.result", "to": "first.value" },
                                { "from": "first.event", "to": "enter.when" },
                                { "from": "enter.order", "to": "position.orders" },
                                { "from": "position.position", "to": "close-after.position" },
                                { "from": "close-after.order", "to": "position.orders" }
                              ]
                            }
                            """;
        using var document = JsonDocument.Parse(json);
        StrategyDocument strategy = await BenchServices.Get<IStrategyRepository>().CreateAsync(
            StrategyDocument.Create("close-up", null, StrategyGraph.ReadForm(document.RootElement).Value!, new Dictionary<string, NodePosition>(), StrategyKind.Idea),
            CancellationToken.None);
        await BenchServices.Get<IStrategyIdeasRepository>().SetAsync(new StrategyIdeas(strategy.Id, true, [TimeSpan.FromMinutes(5)]), CancellationToken.None);
    }
}
