using Dapper;
using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class MigrationTests
{
    [Test]
    public async Task Candles_table_is_a_hypertable()
    {
        await using var dataSource = await MarketDataDatabase.CreateAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        var hypertables = await connection.QueryAsync<string>("SELECT hypertable_name FROM timescaledb_information.hypertables");

        hypertables.ShouldBe(new[] { "candles" });
    }
}
