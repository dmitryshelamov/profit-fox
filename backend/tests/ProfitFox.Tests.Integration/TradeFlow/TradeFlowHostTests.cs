using System.Globalization;

using Dapper;

using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.TradeFlow;

using Shouldly;

namespace ProfitFox.Tests.Integration.TradeFlow;

/// <summary>The collector process against the fake Bybit, on Postgres and the test's clock (task 6.1 of collect-trade-flow).</summary>
public class TradeFlowHostTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-10T03:00:10Z", CultureInfo.InvariantCulture);

    private NpgsqlDataSource _database = null!;
    private FakeTimeProvider _time = null!;
    private readonly List<IAsyncDisposable> _disposables = [];

    [SetUp]
    public async Task SetUp()
    {
        _database = NpgsqlDataSource.Create(IntegrationEnvironment.ConnectionString);
        _time = new FakeTimeProvider(Start);
        await new ScannerPairRepository(_database).SaveAsync(
            [
                new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true),
                new ScannerPair(new Instrument(MarketCategory.Spot, "BTCUSDT"), true),
                new ScannerPair(new Instrument(MarketCategory.Linear, "OLDUSDT"), false)
            ],
            CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IAsyncDisposable disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync();
        }

        _disposables.Clear();
        await _database.DisposeAsync();
    }

    // Scenario "Поток сделок без сканера": the pairs of the list of the scanner, with no scanner running.
    [Test]
    public async Task Collector_writes_the_minutes_of_the_pairs_of_the_scanner_list_without_a_scanner()
    {
        (FakeBybitSocketServer sockets, FakeBybitArchiveServer archive) = await BybitAsync();
        await StartAsync(sockets, archive);
        await WaitAsync(() => Task.FromResult(sockets.Subscribed("publicTrade.SOLUSDT") && sockets.Subscribed("publicTrade.BTCUSDT")));

        sockets.Subscribed("publicTrade.OLDUSDT").ShouldBeFalse();
        DateTimeOffset at = _time.GetUtcNow();
        await sockets.PushAsync("publicTrade.SOLUSDT", [FakeBybitSocketServer.Trade("SOLUSDT", "Buy", 2m, 150m, at), FakeBybitSocketServer.Trade("SOLUSDT", "Sell", 1m, 149m, at)]);
        await sockets.PushAsync("publicTrade.BTCUSDT", [FakeBybitSocketServer.Trade("BTCUSDT", "Sell", 0.5m, 70000m, at)]);

        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM trade_flow_minutes") == 2);
        (await ScalarAsync<decimal>("SELECT buy_turnover FROM trade_flow_minutes WHERE symbol = 'SOLUSDT'")).ShouldBe(300m);
        (await ScalarAsync<decimal>("SELECT sell_turnover FROM trade_flow_minutes WHERE symbol = 'SOLUSDT'")).ShouldBe(149m);
        (await ScalarAsync<decimal>("SELECT sell_volume FROM trade_flow_minutes WHERE category = 'spot' AND symbol = 'BTCUSDT'")).ShouldBe(0.5m);
    }

    // Requirement "Один держатель потока сделок", scenarios "Два сборщика" and "Держатель упал".
    [Test]
    public async Task Only_one_of_two_collectors_holds_the_flow_and_the_other_takes_it_once_the_lease_expires()
    {
        (FakeBybitSocketServer firstSockets, FakeBybitArchiveServer firstArchive) = await BybitAsync();
        (FakeBybitSocketServer secondSockets, FakeBybitArchiveServer secondArchive) = await BybitAsync();
        TradeFlowHost first = await StartAsync(firstSockets, firstArchive);
        await WaitAsync(() => Task.FromResult(firstSockets.Subscribed("publicTrade.SOLUSDT")));
        await StartAsync(secondSockets, secondArchive);

        await AdvanceAsync(TimeSpan.FromSeconds(20));

        (secondSockets.Connections("/v5/public/linear") + secondSockets.Connections("/v5/public/spot")).ShouldBe(0);
        secondArchive.Requests.ShouldBeEmpty();
        firstArchive.Requests.ShouldNotBeEmpty();

        await first.StopAsync();
        await WaitAsync(() => Task.FromResult(secondSockets.Subscribed("publicTrade.SOLUSDT")));
    }

    // Scenario "Ночная пересборка" through the process: the day of the reference file is rebuilt from the archive.
    [Test]
    public async Task Closed_day_is_rebuilt_from_the_archive()
    {
        (FakeBybitSocketServer sockets, FakeBybitArchiveServer archive) = await BybitAsync();
        archive.Serve("/trading/SOLUSDT/SOLUSDT2026-11-09.csv.gz", TradeFlowFixtures.Day("linear"));
        await StartAsync(sockets, archive);

        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM trade_flow_days WHERE status = 'rebuilt'") == 1);
        (await ScalarAsync<int>("SELECT count(*) FROM trade_flow_minutes WHERE symbol = 'SOLUSDT' AND category = 'linear'")).ShouldBe(2);
        (await ScalarAsync<DateTime>("SELECT day FROM trade_flow_days WHERE status = 'rebuilt'")).ShouldBe(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc));
    }

    private async Task<(FakeBybitSocketServer, FakeBybitArchiveServer)> BybitAsync()
    {
        FakeBybitSocketServer sockets = await FakeBybitSocketServer.StartAsync();
        FakeBybitArchiveServer archive = await FakeBybitArchiveServer.StartAsync();
        _disposables.Add(sockets);
        _disposables.Add(archive);

        return (sockets, archive);
    }

    private async Task<TradeFlowHost> StartAsync(FakeBybitSocketServer sockets, FakeBybitArchiveServer archive)
    {
        var host = new TradeFlowHost(IntegrationEnvironment.ConnectionString, sockets, archive, _time);
        _disposables.Add(host);
        await host.StartAsync();

        return host;
    }

    // Moves the clock a second at a time, giving the collector a moment to react after each step.
    private async Task AdvanceAsync(TimeSpan span)
    {
        for (TimeSpan moved = TimeSpan.Zero; moved < span; moved += TimeSpan.FromSeconds(1))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }
    }

    private async Task WaitAsync(Func<Task<bool>> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(60);

        while (!await condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();

        return (await connection.ExecuteScalarAsync<T>(sql))!;
    }
}