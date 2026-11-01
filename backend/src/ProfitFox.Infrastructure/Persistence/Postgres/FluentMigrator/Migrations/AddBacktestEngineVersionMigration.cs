using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611030002)]
public sealed class AddBacktestEngineVersionMigration : ForwardOnlyMigration
{
    // Existing backtests were computed by the first engine version; a new one always names its version.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE backtests ADD COLUMN engine_version integer NOT NULL DEFAULT 1;
            ALTER TABLE backtests ALTER COLUMN engine_version DROP DEFAULT;
            """);
    }
}