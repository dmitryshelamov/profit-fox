using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611010001)]
public sealed class AddCandleImportJobInterruptionsMigration : ForwardOnlyMigration
{
    // Existing jobs have never been interrupted by the rule that counts interruptions.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE candle_import_jobs ADD COLUMN interruptions_without_progress integer NOT NULL DEFAULT 0;
            """);
    }
}