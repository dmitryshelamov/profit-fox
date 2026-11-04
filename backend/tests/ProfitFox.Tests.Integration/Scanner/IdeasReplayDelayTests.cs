using System.Diagnostics;
using System.Globalization;

using Dapper;

using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Integration.Persistence;

using Shouldly;

namespace ProfitFox.Tests.Integration.Scanner;

/// <summary>
///     Requirement "Прогон идей в фоне сканера" and task 5.4: while the replay of history runs, the snapshot of a minute is written at most 5
///     seconds of real time after it is due. Four pairs have 20 days of stored minutes that swing, so the replay has work for the whole test.
/// </summary>
public class IdeasReplayDelayTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:30Z", CultureInfo.InvariantCulture);
    private static readonly string[] Symbols = ["SOLUSDT", "WIFUSDT", "ETHUSDT", "XRPUSDT"];
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(5);
    private const int Days = 20;

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

    [Test]
    public async Task Snapshot_comes_in_time_while_history_is_replayed()
    {
        DateTimeOffset end = Floor(Start);
        await SeedAsync(end);
        var bybit = new FakeBybitHandler();

        foreach (string symbol in Symbols)
        {
            bybit.Instruments.Add(("linear", symbol));
            bybit.Coins[("linear", symbol)] = (symbol[..^4], "USDT");
            bybit.Tickers[("linear", symbol)] = ("100", "99.9", "100.1", "1000", "0.0001");
        }

        FakeBybitSocketServer sockets = await FakeBybitSocketServer.StartAsync();
        _disposables.Add(sockets);
        var scanner = new ScannerHost(
            _connectionString,
            bybit,
            sockets,
            _time,
            new Dictionary<string, string?> { ["Scanner:HistoryDays"] = $"{Days}", ["Scanner:Symbols:0"] = "SOLUSDT", ["Scanner:Symbols:1"] = "WIFUSDT", ["Scanner:Symbols:2"] = "ETHUSDT", ["Scanner:Symbols:3"] = "XRPUSDT" });
        _disposables.Add(scanner);
        await scanner.StartAsync();
        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM idea_replay") > 0);
        var delays = new List<TimeSpan>();

        for (int minute = 0; minute < 5; minute++)
        {
            DateTime before = await ScalarAsync<DateTime>("SELECT coalesce(snapshot_at, '-infinity') FROM scanner_state");
            DateTimeOffset due = Floor(_time.GetUtcNow()).AddMinutes(1).AddSeconds(10);

            while (_time.GetUtcNow() < due - TimeSpan.FromSeconds(1))
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(10);
            }

            // The last second makes the snapshot due: real time counts from here.
            _time.Advance(due - _time.GetUtcNow());
            var real = Stopwatch.StartNew();

            while (await ScalarAsync<DateTime>("SELECT snapshot_at FROM scanner_state") <= before && real.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(20);
            }

            delays.Add(real.Elapsed);
            await Task.Delay(100);
        }

        await TestContext.Out.WriteLineAsync($"Delays of the snapshot: {string.Join(", ", delays.Select(delay => delay.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)))} s");
        await TestContext.Out.WriteLineAsync($"Replayed: {await ScalarAsync<string>("SELECT string_agg(symbol || ' ' || replayed_until::text, '; ') FROM idea_replay")}");

        // The replay was at work all along: it has not reached the end of every pair yet.
        (await ScalarAsync<int>("SELECT count(*) FROM idea_replay WHERE replayed_until >= @End", new { End = Floor(end - TimeSpan.FromDays(1)) })).ShouldBeLessThan(Symbols.Length);
        delays.ShouldAllBe(delay => delay <= MaximumDelay, $"Delays of the snapshot: {string.Join(", ", delays.Select(delay => delay.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)))} s");
    }

    // The pairs with their history loaded whole up to the end, and their minutes swinging by a few percent over the hours.
    private async Task SeedAsync(DateTimeOffset end)
    {
        var pairs = new ScannerPairRepository(_database);
        var candles = new CandleRepository(_database);
        DateTimeOffset from = end.AddDays(-Days);
        await pairs.SaveAsync([.. Symbols.Select(symbol => new ScannerPair(new Instrument(MarketCategory.Linear, symbol), true, HistoryFrom: from, CoveredUntil: end))], CancellationToken.None);

        foreach (string symbol in Symbols)
        {
            var instrument = new Instrument(MarketCategory.Linear, symbol);
            var batch = new List<Candle>();
            decimal price = 100m;

            for (DateTimeOffset time = from; time < end; time = time.AddMinutes(1))
            {
                var index = (int)(time - from).TotalMinutes;
                decimal step = (index / 37 % 2 == 0 ? 0.03m : -0.03m) * (1 + (index / 211 % 3)) * (1 + (index / 1440 % 2));
                batch.Add(new Candle(time, new Price(price), new Price(price + Math.Abs(step) + 0.01m), new Price(price - 0.01m), new Price(price + step), new Volume(1m), new Volume(100m + (index % 97))));
                price += step;

                if (batch.Count == 10000)
                {
                    await candles.UpsertAsync(instrument, Interval.OneMinute, batch, CancellationToken.None);
                    batch.Clear();
                }
            }

            await candles.UpsertAsync(instrument, Interval.OneMinute, batch, CancellationToken.None);
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

            await Task.Delay(50);
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