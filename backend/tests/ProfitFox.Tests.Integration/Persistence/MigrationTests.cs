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
}