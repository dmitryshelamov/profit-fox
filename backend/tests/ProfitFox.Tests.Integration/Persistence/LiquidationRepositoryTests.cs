using System.Globalization;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class LiquidationRepositoryTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-08T12:04:07.350Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");

    private NpgsqlDataSource _dataSource = null!;
    private LiquidationRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _store = new LiquidationRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Ликвидация пришла".
    [Test]
    public async Task Event_is_read_back_with_the_same_fields()
    {
        var liquidation = new LiquidationEvent(Start, LiquidationSide.Long, 150.2m, 12.5m);

        await _store.AddAsync([(Sol, liquidation)], CancellationToken.None);

        (await _store.GetAsync(Sol, Start, Start.AddSeconds(1), 1000, CancellationToken.None)).ShouldBe([liquidation]);
    }

    [Test]
    public async Task Insert_tells_the_rows_it_added_of_each_side()
    {
        IReadOnlyDictionary<LiquidationSide, int> inserted = await _store.AddAsync(
        [
            (Sol, new LiquidationEvent(Start, LiquidationSide.Long, 150.2m, 1m)),
            (Sol, new LiquidationEvent(Start, LiquidationSide.Long, 150.1m, 2m)),
            (Btc, new LiquidationEvent(Start, LiquidationSide.Long, 70000m, 0.01m)),
            (Btc, new LiquidationEvent(Start, LiquidationSide.Short, 70010m, 0.02m))
        ],
            CancellationToken.None);

        inserted.ShouldBe(new Dictionary<LiquidationSide, int> { [LiquidationSide.Long] = 3, [LiquidationSide.Short] = 1 }, ignoreOrder: true);
    }

    // Scenario "Повтор события": Bybit sends the last batch again after a reconnect.
    [Test]
    public async Task Event_written_again_stays_one_row_and_is_not_counted()
    {
        var liquidation = new LiquidationEvent(Start, LiquidationSide.Short, 150.2m, 12.5m);
        await _store.AddAsync([(Sol, liquidation)], CancellationToken.None);

        IReadOnlyDictionary<LiquidationSide, int> inserted = await _store.AddAsync([(Sol, liquidation)], CancellationToken.None);

        inserted.ShouldBeEmpty();
        (await _store.GetAsync(Sol, Start, Start.AddSeconds(1), 1000, CancellationToken.None)).ShouldBe([liquidation]);
    }

    [Test]
    public async Task Read_takes_the_events_of_the_instrument_in_the_period_oldest_first_up_to_the_limit()
    {
        LiquidationEvent[] events = [.. Enumerable.Range(-1, 5).Select(i => new LiquidationEvent(Start.AddSeconds(i), LiquidationSide.Long, 150m + i, 1m))];
        await _store.AddAsync([.. events.Reverse().Select(liquidation => (Sol, liquidation)), (Btc, new LiquidationEvent(Start, LiquidationSide.Long, 70000m, 1m))], CancellationToken.None);

        IReadOnlyList<LiquidationEvent> all = await _store.GetAsync(Sol, Start, Start.AddSeconds(3), 1000, CancellationToken.None);
        IReadOnlyList<LiquidationEvent> limited = await _store.GetAsync(Sol, Start, Start.AddSeconds(3), 2, CancellationToken.None);

        all.ShouldBe(events[1..4]);
        limited.ShouldBe(events[1..3]);
    }

    // Scenario "Старые события": a chunk older than 7 days is compressed and still read.
    [Test]
    public async Task Event_of_a_compressed_chunk_is_read_with_the_same_fields()
    {
        var liquidation = new LiquidationEvent(Start.AddDays(-10), LiquidationSide.Long, 150.2m, 12.5m);
        await _store.AddAsync([(Sol, liquidation)], CancellationToken.None);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("SELECT compress_chunk(c) FROM show_chunks('liquidations', older_than => INTERVAL '7 days') c");
        bool compressed = await connection.QuerySingleAsync<bool>(
            "SELECT bool_and(is_compressed) FROM timescaledb_information.chunks WHERE hypertable_name = 'liquidations'");

        compressed.ShouldBeTrue();
        (await _store.GetAsync(Sol, liquidation.Time, liquidation.Time.AddSeconds(1), 1000, CancellationToken.None)).ShouldBe([liquidation]);
    }

    [Test]
    public async Task Last_time_is_none_without_events()
    {
        (await _store.LastTimeAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Last_time_is_that_of_the_latest_event_of_any_instrument()
    {
        await _store.AddAsync(
        [
            (Btc, new LiquidationEvent(Start.AddSeconds(5), LiquidationSide.Long, 70000m, 1m)),
            (Sol, new LiquidationEvent(Start, LiquidationSide.Long, 150m, 1m))
        ],
            CancellationToken.None);

        (await _store.LastTimeAsync(CancellationToken.None)).ShouldBe(Start.AddSeconds(5));
    }
}