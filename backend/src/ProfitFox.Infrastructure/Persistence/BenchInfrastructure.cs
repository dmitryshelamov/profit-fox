using FluentMigrator.Runner;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Ports.Hypotheses;
using ProfitFox.Application.Ports.Runs;
using ProfitFox.Application.Ports.Signals;
using ProfitFox.Infrastructure.Hypotheses;
using ProfitFox.Infrastructure.Ledger;
using ProfitFox.Infrastructure.Runs;
using ProfitFox.Infrastructure.Signals;

namespace ProfitFox.Infrastructure.Persistence;

/// <summary>
///     The database of the bench: its stores, its migrations. The market-data service is not part of it.
/// </summary>
public static class BenchInfrastructure
{
    public static IServiceCollection AddBenchDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IHypothesisStore, HypothesisStore>();
        services.AddSingleton<IRunLedger, RunLedger>();
        services.AddSingleton<IRunQueue, RunQueue>();
        services.AddSingleton<ISignalStore, SignalStore>();
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(BenchInfrastructure).Assembly)
                .For.Migrations());
        return services;
    }

    /// <summary>
    ///     Applies every migration the database does not have yet.
    /// </summary>
    public static void MigrateBench(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
