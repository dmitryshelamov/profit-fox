using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611010001)]
public sealed class AddCandleImportJobStalledRestartsMigration : ForwardOnlyMigration
{
    // Existing jobs have no stalled restart counted yet.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE candle_import_jobs ADD COLUMN stalled_restarts integer NOT NULL DEFAULT 0;
            """);
    }
}