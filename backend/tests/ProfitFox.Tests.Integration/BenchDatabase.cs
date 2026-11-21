using Dapper;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator;

using Testcontainers.PostgreSql;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     Starts one Postgres container for the whole test assembly and migrates its database; <see cref="Reset" /> empties the
///     tables before a test.
/// </summary>
[SetUpFixture]
public sealed class BenchDatabase
{
    // The image of the bench database in production (design add-bench-service, decision 13).
    private const string Image = "postgres:17";

    private static PostgreSqlContainer _container = null!;
    private static NpgsqlDataSource _dataSource = null!;

    /// <summary>
    ///     The connection string, with its password, of the migrated test database.
    /// </summary>
    public static string ConnectionString => _container.GetConnectionString();

    public static NpgsqlDataSource DataSource => _dataSource;

    [OneTimeSetUp]
    public async Task Start()
    {
        // A test database needs no durability: without syncing to disk the tests run faster.
        _container = new PostgreSqlBuilder(Image)
            .WithCommand("-c", "fsync=off", "-c", "full_page_writes=off")
            .Build();
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(ConnectionString);
        await using ServiceProvider services = Migrated(ConnectionString);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    ///     Migrates the database of <paramref name="connectionString" /> and gives the services that did it.
    /// </summary>
    public static ServiceProvider Migrated(string connectionString)
    {
        var registrations = new ServiceCollection();
        PostgresRegistrar.Configure(Settings(connectionString), registrations);
        ServiceProvider services = registrations.BuildServiceProvider();
        services.MigrateBench();
        return services;
    }

    /// <summary>
    ///     The settings of the bench over the database of <paramref name="connectionString" />, with addresses of a market-data
    ///     service that no test reaches: a test host replaces the market-data service after registration.
    /// </summary>
    public static IConfiguration Settings(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bench"] = connectionString,
                ["MarketData:GrpcUrl"] = "http://localhost:1",
                ["MarketData:RestUrl"] = "http://localhost:2/"
            })
            .Build();
    }

    /// <summary>
    ///     A new empty database in the container, to try a migration on.
    /// </summary>
    public static async Task<string> NewDatabase()
    {
        string name = $"bench_{Guid.NewGuid():N}";
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync($"CREATE DATABASE {name}");
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>
    ///     Removes the database of <paramref name="connectionString" /> made by <see cref="NewDatabase" />, closing the
    ///     connections it has.
    /// </summary>
    public static async Task DropDatabase(string connectionString)
    {
        string name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync($"DROP DATABASE {name} WITH (FORCE)");
    }

    /// <summary>
    ///     Empties every table the migrations made. The ledger refuses to change a row, but not to be emptied as a whole.
    /// </summary>
    public static async Task Reset()
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("TRUNCATE hypothesis_versions, signals, signal_versions, strategies, strategy_versions, strategy_signals, runs, ledger, reports");
    }
}
