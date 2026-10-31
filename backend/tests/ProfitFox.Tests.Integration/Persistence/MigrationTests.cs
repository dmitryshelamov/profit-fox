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