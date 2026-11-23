using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611230002)]
public sealed class AddScannerFailuresMigration : Migration
{
    // What the holder of the lease of the scanner tells of the warm-ups that failed (change add-ideas-and-market, task
    // 18.3): how many pairs and the last reason.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE scanner_lease
                ADD COLUMN failed integer NOT NULL DEFAULT 0,
                ADD COLUMN failure text NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE scanner_lease DROP COLUMN failure, DROP COLUMN failed;");
    }
}
