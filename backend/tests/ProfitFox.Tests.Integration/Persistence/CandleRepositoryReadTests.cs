using Npgsql;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class CandleRepositoryReadTests
{
    private static readonly Instrument BtcLinear = new(MarketCategory.Linear, "BTCUSDT");
    private static readonly Interval Hour = Interval.OneHour;

    private NpgsqlDataSource _dataSource = null!;
    private CandleRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new CandleRepository(_dataSource);
        // Stored newest first, the way Bybit pages arrive.
        await _store.UpsertAsync(BtcLinear, Hour, [Candle("2024-01-01T02:00:00Z"), Candle("2024-01-01T01:00:00Z"), Candle("2024-01-01T00:00:00Z")], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown() => await _dataSource.DisposeAsync();

    // Scenario "Свечи есть".
    [Test]
    public async Task Returns_candles_of_the_period_oldest_first()
    {
        var candles = await _store.GetAsync(BtcLinear, Hour, At("2024-01-01T01:00:00Z"), At("2024-01-01T03:00:00Z"), 1000, CancellationToken.None);

        candles.Select(c => c.OpenTime).ShouldBe(new[] { At("2024-01-01T01:00:00Z"), At("2024-01-01T02:00:00Z") });
    }

    [Test]
    public async Task End_of_period_is_exclusive()
    {
        var candles = await _store.GetAsync(BtcLinear, Hour, At("2024-01-01T00:00:00Z"), At("2024-01-01T02:00:00Z"), 1000, CancellationToken.None);

        candles.Count.ShouldBe(2);
    }

    [Test]
    public async Task Returns_at_most_limit_candles_starting_from_the_earliest()
    {
        var candles = await _store.GetAsync(BtcLinear, Hour, At("2024-01-01T00:00:00Z"), At("2024-01-02T00:00:00Z"), 2, CancellationToken.None);

        candles.Select(c => c.OpenTime).ShouldBe(new[] { At("2024-01-01T00:00:00Z"), At("2024-01-01T01:00:00Z") });
    }

    [Test]
    public async Task Returns_full_candle_values()
    {
        var candles = await _store.GetAsync(BtcLinear, Hour, At("2024-01-01T00:00:00Z"), At("2024-01-01T01:00:00Z"), 1000, CancellationToken.None);

        candles.ShouldBe(new[] { Candle("2024-01-01T00:00:00Z") });
    }

    [TestCase(MarketCategory.Spot, "BTCUSDT", Interval.OneHour)]
    [TestCase(MarketCategory.Linear, "ETHUSDT", Interval.OneHour)]
    [TestCase(MarketCategory.Linear, "BTCUSDT", Interval.OneDay)]
    public async Task Returns_only_candles_of_the_requested_instrument_and_interval(MarketCategory category, string symbol, Interval interval)
    {
        var candles = await _store.GetAsync(new Instrument(category, symbol), interval, At("2024-01-01T00:00:00Z"), At("2024-01-02T00:00:00Z"), 1000, CancellationToken.None);

        candles.ShouldBeEmpty();
    }

    private static Candle Candle(string openTime) =>
        new(At(openTime), new Price(1m), new Price(2m), new Price(0.5m), new Price(1.5m), new Volume(0.000123m), new Volume(42123.45m));

    private static DateTimeOffset At(string time) => DateTimeOffset.Parse(time);
}
