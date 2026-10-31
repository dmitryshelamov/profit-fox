using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

public static class FluentMigratorBuilderExtensions
{
    /// <summary>Applies every migration the database does not have yet.</summary>
    public static void MigrateToLatestVersion(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
