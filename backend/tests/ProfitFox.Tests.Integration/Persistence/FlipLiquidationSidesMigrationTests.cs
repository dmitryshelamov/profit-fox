using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     The liquidations stored before the fix of their side carry the opposite side: the stream gives the side of the liquidated position,
///     and the code read it as the side of an order. The migration turns every stored side around once; the deploy stops the scanner
///     before the API migrates, so every row at that moment is from the old code.
/// </summary>
public class FlipLiquidationSidesMigrationTests
{
    // The migration that made the liquidations tables, the last one before the flip.
    private const long LiquidationTables = 202611080005;

    private ServiceProvider _migrations = null!;
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        string connectionString = await IntegrationEnvironment.CreateEmptyDatabaseAsync();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        PostgresRegistrar.Configure(configuration, services);
        _migrations = services.BuildServiceProvider();
        Migrate(LiquidationTables);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Stored_sides_turn_around_once()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO liquidations (category, symbol, "time", side, price, quantity) VALUES
                    ('linear', 'SOLUSDT', '2026-11-07T21:09:14.296Z', 'short', 150.2, 12.5),
                    ('linear', 'SOLUSDT', '2026-11-07T21:09:14.296Z', 'long', 150.2, 12.5),
                    ('linear', 'BTRUSDT', '2026-11-07T21:27:26.389Z', 'short', 0.04045, 18587)
                """);
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        IEnumerable<string> rows = await read.QueryAsync<string>("""SELECT symbol || ' ' || side FROM liquidations ORDER BY "time", side""");

        rows.ShouldBe(["SOLUSDT long", "SOLUSDT short", "BTRUSDT long"]);
    }

    private void Migrate(long? version)
    {
        using IServiceScope scope = _migrations.CreateScope();
        IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        if (version is { } target)
        {
            runner.MigrateUp(target);
        }
        else
        {
            runner.MigrateUp();
        }
    }
}