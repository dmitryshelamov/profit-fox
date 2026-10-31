using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

namespace ProfitFox.Tests.Integration.Persistence;

public static class MarketDataDatabase
{
    /// <summary>A fresh database with all migrations applied.</summary>
    public static async Task<NpgsqlDataSource> CreateAsync()
    {
        var connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        PostgresRegistrar.Configure(configuration, services);
        await using (var provider = services.BuildServiceProvider())
        {
            provider.MigrateToLatestVersion();
        }

        return NpgsqlDataSource.Create(connectionString);
    }
}
