using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611090001)]
public sealed class AddIdeaTradeLeverageMigration : ForwardOnlyMigration
{
    // Decisions 6 and 7: the settings saved before have the leverage of one. The trades saved before have none: their leverage is unknown, they entered
    // at whatever the symbol had on the exchange; a new trade always writes its own. The image before this one reads and writes neither column.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_trading_settings ADD COLUMN leverage int NOT NULL DEFAULT 1;
            ALTER TABLE idea_trades ADD COLUMN leverage int NULL;
            """);
    }
}