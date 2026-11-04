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

        hypertables.ShouldBe(["candles", "open_interest", "idea_cases"], ignoreOrder: true);
    }

    // Jobs created before the column existed must read as never interrupted; the column is now the count of lost leases.
    [Test]
    public async Task Import_jobs_count_lost_leases_from_zero()
    {
        await using NpgsqlDataSource dataSource = await MarketDataDatabase.CreateAsync();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        (string DataType, string IsNullable, string Default) column = await connection.QuerySingleAsync<(string, string, string)>(
            """
            SELECT data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_name = 'candle_import_jobs' AND column_name = 'lost_leases'
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

    // Task 3.1: the tables of the ideas, and the setups of a snapshot row empty until the scanner writes them.
    [Test]
    public async Task Ideas_have_their_tables_and_snapshot_rows_have_no_setups_yet()
    {
        await using NpgsqlDataSource dataSource = await MarketDataDatabase.CreateAsync();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<string> tables = await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_name IN ('ideas', 'idea_cases', 'idea_buckets', 'idea_replay', 'idea_volatility', 'saved_filters')");
        (string DataType, string IsNullable, string Default) setups = await connection.QuerySingleAsync<(string, string, string)>(
            """
            SELECT data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_name = 'market_snapshot' AND column_name = 'setups'
            """);

        tables.Count().ShouldBe(6);
        setups.ShouldBe(("jsonb", "NO", "'{}'::jsonb"));
    }
}