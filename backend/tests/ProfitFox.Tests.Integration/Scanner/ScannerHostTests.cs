using System.Globalization;

using Dapper;

using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Integration.Persistence;

using Shouldly;

namespace ProfitFox.Tests.Integration.Scanner;

/// <summary>The scanner process against the fake Bybit, on Postgres and the test's clock.</summary>
public class ScannerHostTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:30Z", CultureInfo.InvariantCulture);
    private static readonly IReadOnlyDictionary<string, string?> OneDay = new Dictionary<string, string?> { ["Scanner:HistoryDays"] = "1" };

    private string _connectionString = null!;
    private NpgsqlDataSource _database = null!;
    private FakeTimeProvider _time = null!;
    private readonly List<IAsyncDisposable> _disposables = [];

    [SetUp]
    public async Task SetUp()
    {
        _connectionString = await MarketDataDatabase.CreateConnectionStringAsync();
        _database = NpgsqlDataSource.Create(_connectionString);
        _time = new FakeTimeProvider(Start);
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

    // Requirements "Живые минутные свечи", "Снимок рынка" and "Состояние сканера", decision 8: on an empty database.
    [Test]
    public async Task Scanner_on_an_empty_database_loads_the_day_keeps_the_stream_and_saves_the_snapshot()
    {
        (FakeBybitHandler bybit, FakeBybitSocketServer sockets) = await BybitAsync();
        await StartScannerAsync(bybit, sockets);

        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM market_snapshot") == 2);
        (await ScalarAsync<int>("SELECT pairs_total FROM scanner_state")).ShouldBe(2);
        (await ScalarAsync<int>("SELECT pairs_backfilled FROM scanner_state")).ShouldBe(2);
        (await ScalarAsync<int>("SELECT count(*) FROM candles WHERE \"interval\" = '1' AND symbol = 'SOLUSDT'")).ShouldBe(1440);
        (await ScalarAsync<int>("SELECT count(*) FROM candles WHERE \"interval\" = '1' AND symbol = 'BTCUSDT'")).ShouldBe(1440);
        DateTime firstSnapshot = await ScalarAsync<DateTime>("SELECT snapshot_at FROM scanner_state");

        // Scenario "Минута закрылась": the closed minute is stored once, the forming one never.
        DateTimeOffset minute = Floor(_time.GetUtcNow());
        await sockets.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(minute, TimeSpan.FromMinutes(1), 151m, confirm: true)]);
        await sockets.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(minute, TimeSpan.FromMinutes(1), 151m, confirm: true)]);
        await sockets.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(minute.AddMinutes(1), TimeSpan.FromMinutes(1), 152m, confirm: false)]);

        await WaitAsync(async () => await ScalarAsync<DateTime>("SELECT snapshot_at FROM scanner_state") > firstSnapshot
                                    && await ScalarAsync<int>("SELECT count(*) FROM candles WHERE symbol = 'SOLUSDT' AND open_time = @Minute", new { Minute = minute }) == 1);
        (await ScalarAsync<decimal>("SELECT close FROM candles WHERE symbol = 'SOLUSDT' AND open_time = @Minute", new { Minute = minute })).ShouldBe(151m);
        (await ScalarAsync<int>("SELECT count(*) FROM candles WHERE symbol = 'SOLUSDT' AND open_time = @Minute", new { Minute = minute.AddMinutes(1) })).ShouldBe(0);
    }

    // Requirement "Один держатель потока", scenarios "Два сканера" and "Держатель упал".
    [Test]
    public async Task Only_one_of_two_scanners_holds_the_stream_and_the_other_takes_it_once_the_lease_expires()
    {
        (FakeBybitHandler firstBybit, FakeBybitSocketServer firstSockets) = await BybitAsync();
        (FakeBybitHandler secondBybit, FakeBybitSocketServer secondSockets) = await BybitAsync();
        ScannerHost first = await StartScannerAsync(firstBybit, firstSockets);
        await WaitAsync(() => Task.FromResult(firstSockets.Subscribed("kline.1.SOLUSDT")));
        await StartScannerAsync(secondBybit, secondSockets);

        await AdvanceAsync(TimeSpan.FromSeconds(20));

        (secondSockets.Connections("/v5/public/linear") + secondSockets.Connections("/v5/public/spot")).ShouldBe(0);
        secondBybit.Requests.ShouldBe(0);

        await first.StopAsync();
        await WaitAsync(() => Task.FromResult(secondSockets.Subscribed("kline.1.SOLUSDT")));
        await WaitAsync(() => Task.FromResult(secondBybit.KlineRequests.Count > 0));
    }

    // Scenario "Соединение пропало на 3 минуты": the minutes of the break are loaded.
    [Test]
    public async Task Minutes_missed_while_the_stream_was_away_are_loaded()
    {
        (FakeBybitHandler bybit, FakeBybitSocketServer sockets) = await BybitAsync();
        await StartScannerAsync(bybit, sockets);
        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM market_snapshot") == 2);
        DateTimeOffset gap = Floor(_time.GetUtcNow());

        await sockets.DropAsync();
        await AdvanceAsync(TimeSpan.FromMinutes(3));

        await WaitAsync(() => Task.FromResult(
            Enumerable.Range(0, 3).All(minute => bybit.KlineRequests.ToList().Any(request => request.Start <= gap.AddMinutes(minute) && request.End >= gap.AddMinutes(minute)))));
        await WaitAsync(() => Task.FromResult(sockets.Subscribed("kline.1.SOLUSDT")));
    }

    // Scenario "Новая пара".
    [Test]
    public async Task New_pair_is_subscribed_once_the_list_is_read_again()
    {
        (FakeBybitHandler bybit, FakeBybitSocketServer sockets) = await BybitAsync();
        // The list is read again each minute instead of each hour, so the clock moves a minute, not an hour, a second at a time.
        await StartScannerAsync(bybit, sockets, new Dictionary<string, string?>(OneDay) { ["Scanner:UniverseRefresh"] = "00:01:00" });
        await WaitAsync(() => Task.FromResult(sockets.Subscribed("kline.1.SOLUSDT")));

        bybit.Instruments.Add(("linear", "NEWUSDT"));
        bybit.Coins[("linear", "NEWUSDT")] = ("NEW", "USDT");
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        await WaitAsync(() => Task.FromResult(sockets.Subscribed("kline.1.NEWUSDT")));
    }

    // A failed step does not stop the scanner.
    [Test]
    public async Task Scanner_goes_on_once_bybit_answers_again()
    {
        (FakeBybitHandler bybit, FakeBybitSocketServer sockets) = await BybitAsync();
        bybit.Unavailable = true;
        await StartScannerAsync(bybit, sockets);

        await AdvanceAsync(TimeSpan.FromMinutes(2));
        bybit.Unavailable = false;

        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM market_snapshot") == 2);
    }

    private async Task<(FakeBybitHandler, FakeBybitSocketServer)> BybitAsync()
    {
        var bybit = new FakeBybitHandler();
        bybit.Instruments.UnionWith([("linear", "SOLUSDT"), ("spot", "BTCUSDT")]);
        bybit.Coins[("linear", "SOLUSDT")] = ("SOL", "USDT");
        bybit.Coins[("spot", "BTCUSDT")] = ("BTC", "USDT");
        bybit.Tickers[("linear", "SOLUSDT")] = ("150", "149.9", "150.1", "1000", "0.0001");
        bybit.Tickers[("spot", "BTCUSDT")] = ("64000", "63999", "64001", "", "");
        DateTimeOffset end = Floor(Start);

        for (DateTimeOffset time = end.AddDays(-1); time < end; time = time.AddMinutes(1))
        {
            bybit.Klines.Add([time.ToString("O", CultureInfo.InvariantCulture), "100", "101", "99", "100", "1", "100"]);
        }

        FakeBybitSocketServer sockets = await FakeBybitSocketServer.StartAsync();
        _disposables.Add(sockets);

        return (bybit, sockets);
    }

    private async Task<ScannerHost> StartScannerAsync(FakeBybitHandler bybit, FakeBybitSocketServer sockets, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var scanner = new ScannerHost(_connectionString, bybit, sockets, _time, settings ?? OneDay);
        _disposables.Add(scanner);
        await scanner.StartAsync();

        return scanner;
    }

    // Moves the clock a second at a time, giving the scanner a moment to react after each step.
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

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();

        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static DateTimeOffset Floor(DateTimeOffset time)
    {
        return time.AddTicks(-(time.UtcTicks % TimeSpan.TicksPerMinute));
    }
}