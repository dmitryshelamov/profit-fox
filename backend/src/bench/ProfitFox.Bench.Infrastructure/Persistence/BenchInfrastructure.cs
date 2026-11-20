using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Application.Signals;
using ProfitFox.Bench.Infrastructure.Hypotheses;
using ProfitFox.Bench.Infrastructure.Ledger;
using ProfitFox.Bench.Infrastructure.Runs;
using ProfitFox.Bench.Infrastructure.Signals;

namespace ProfitFox.Bench.Infrastructure.Persistence;

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
