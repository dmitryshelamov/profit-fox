using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611250002)]
public sealed class AddScannerWarmupLeftMigration : Migration
{
    // How long the warm-up of the holder of the lease has still to go, in seconds: null when it is not known or over.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE scanner_lease ADD COLUMN warmup_left_seconds integer NULL;");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE scanner_lease DROP COLUMN warmup_left_seconds;");
    }
}
