using FluentMigrator.Runner;

using Microsoft.Extensions.DependencyInjection;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator;

/// <summary>
///     The migrations of the database of the bench, which the API applies at start.
/// </summary>
public static class BenchMigrations
{
    /// <summary>
    ///     Applies every migration the database does not have yet.
    /// </summary>
    public static void MigrateBench(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
