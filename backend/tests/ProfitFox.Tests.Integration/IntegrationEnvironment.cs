using Npgsql;

using ProfitFox.Tests.Shared.Network;

using Testcontainers.PostgreSql;

namespace ProfitFox.Tests.Integration;

/// <summary>Starts one TimescaleDB container for the whole test assembly; each test gets its own database.</summary>
[SetUpFixture]
public class IntegrationEnvironment
{
    // Keep in sync with docker-compose.yml.
    public const string TimescaleImage = "timescale/timescaledb:2.30.2-pg17";

    private static PostgreSqlContainer Container = null!;

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        OutboundHttpGuard.Install();
        // Every test gets its own database, and idle pooled connections of finished tests stay open for a while.
        Container = new PostgreSqlBuilder(TimescaleImage).WithCommand("-c", "max_connections=1000").Build();
        await Container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        await Container.DisposeAsync();
    }

    public static async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(Container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}