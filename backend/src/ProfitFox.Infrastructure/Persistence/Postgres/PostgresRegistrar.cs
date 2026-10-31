using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;

namespace ProfitFox.Infrastructure.Persistence.Postgres;

internal static class PostgresRegistrar
{
    /// <summary>The connection string is read on first use, so a test host can still replace it after registration.</summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(ConnectionString(configuration)));
        services.AddScoped<ICandleRepository, CandleRepository>();
        services.AddScoped<ICandleImportJobRepository, CandleImportJobRepository>();
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(_ => ConnectionString(configuration))
                .ScanIn(typeof(PostgresRegistrar).Assembly)
                .For.Migrations());
    }

    private static string ConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("MarketData")!;
    }
}