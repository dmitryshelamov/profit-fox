using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

namespace ProfitFox.Infrastructure.Persistence.Postgres;

internal static class PostgresRegistrar
{
    /// <summary>
    ///     The database of the bench (<c>ConnectionStrings:Bench</c>): its repositories and its migrations. A repository holds only
    ///     the data source and makes each call whole in a transaction of its own, so it is one for the process. The market-data
    ///     service is not part of it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection string is not set.</exception>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        string connectionString = InfrastructureRegistrar.Setting(configuration, "ConnectionStrings:Bench");
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IBacktestRepository, BacktestRepository>();
        services.AddSingleton<ISignalRepository, SignalRepository>();
        services.AddSingleton<IStrategyRepository, StrategyRepository>();
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(PostgresRegistrar).Assembly)
                .For.Migrations());
    }
}
