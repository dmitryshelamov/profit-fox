using Dapper;

using Npgsql;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class MigrationTests
{
    [Test]
    public async Task Candles_table_is_a_hypertable()
    {
        await using NpgsqlDataSource dataSource = await MarketDataDatabase.CreateAsync();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<string> hypertables = await connection.QueryAsync<string>("SELECT hypertable_name FROM timescaledb_information.hypertables");

        hypertables.ShouldBe(new[] { "candles" });
    }

    // Jobs created before the column existed must read as never interrupted.
    [Test]
    public async Task Import_jobs_count_stalled_restarts_from_zero()
    {
        await using NpgsqlDataSource dataSource = await MarketDataDatabase.CreateAsync();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        (string DataType, string IsNullable, string Default) column = await connection.QuerySingleAsync<(string, string, string)>(
            """
            SELECT data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_name = 'candle_import_jobs' AND column_name = 'stalled_restarts'
            """);

        column.ShouldBe(("integer", "NO", "0"));
    }

    [Test]
    public async Task Strategy_library_starts_at_revision_zero_without_graphs()
    {
        await using NpgsqlDataSource dataSource = await MarketDataDatabase.CreateAsync();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        (await connection.QueryAsync<long>("SELECT revision FROM strategy_library")).ShouldBe([0L]);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM strategies")).ShouldBe(0L);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM strategy_composite_nodes")).ShouldBe(0L);
    }
}