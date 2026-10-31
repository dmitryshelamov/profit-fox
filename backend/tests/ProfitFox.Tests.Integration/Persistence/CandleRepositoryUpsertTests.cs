using Dapper;
using Npgsql;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;
using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class CandleRepositoryUpsertTests
{
    private static readonly Interval Day = Interval.OneDay;
    private static readonly Interval Hour = Interval.OneHour;

    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp() => _dataSource = await MarketDataDatabase.CreateAsync();

    [TearDown]
    public async Task TearDown() => await _dataSource.DisposeAsync();

    // Scenario "Повторная загрузка".
    [Test]
    public async Task Loading_the_same_period_twice_keeps_one_candle_per_day_with_latest_values()
    {
        var ethSpot = new Instrument(MarketCategory.Spot, "ETHUSDT");
        var store = new CandleRepository(_dataSource);
        await store.UpsertAsync(ethSpot, Day, [Candle("2024-01-01T00:00:00Z", close: 1m), Candle("2024-01-02T00:00:00Z", close: 2m)], CancellationToken.None);

        await store.UpsertAsync(ethSpot, Day, [Candle("2024-01-01T00:00:00Z", close: 10m), Candle("2024-01-02T00:00:00Z", close: 20m)], CancellationToken.None);

        var closes = await QueryAsync<decimal>("SELECT close FROM candles ORDER BY open_time");
        closes.ShouldBe(new[] { 10m, 20m });
    }

    // Scenario "Один символ в разных категориях".
    [Test]
    public async Task Same_symbol_in_spot_and_linear_is_stored_independently()
    {
        var store = new CandleRepository(_dataSource);
        var candles = new[] { Candle("2024-01-01T00:00:00Z", close: 1m) };

        await store.UpsertAsync(new Instrument(MarketCategory.Spot, "BTCUSDT"), Hour, candles, CancellationToken.None);
        await store.UpsertAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), Hour, candles, CancellationToken.None);

        var categories = await QueryAsync<string>("SELECT category FROM candles ORDER BY category");
        categories.ShouldBe(new[] { "linear", "spot" });
    }

    // Scenario "Точность значений".
    [Test]
    public async Task Values_are_stored_exactly()
    {
        var store = new CandleRepository(_dataSource);
        var candle = new Candle(
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            new Price(42000.1m), new Price(42300.5m), new Price(41950m), new Price(42123.45m), new Volume(0.000123m), new Volume(5.18m));

        await store.UpsertAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), Hour, [candle], CancellationToken.None);

        var row = (await QueryAsync<CandleDb>(
            """SELECT category, symbol, "interval", open_time AS "OpenTime", open, high, low, close, volume, turnover FROM candles""")).Single();
        (new[] { row.Open, row.High, row.Low, row.Close, row.Volume, row.Turnover }.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))).ShouldBe(new[] { "42000.1", "42300.5", "41950", "42123.45", "0.000123", "5.18" });
        row.OpenTime.ShouldBe(candle.OpenTime);
        (row.Symbol, row.Interval).ShouldBe(("BTCUSDT", "60"));
    }

    private async Task<List<T>> QueryAsync<T>(string sql)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<T>(sql)).ToList();
    }

    private static Candle Candle(string openTime, decimal close) => CandleGenerator.Generate(DateTimeOffset.Parse(openTime), new Price(close));
}
