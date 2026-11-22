using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611220001)]
public sealed class AddStrategyKindMigration : Migration
{
    // The kind of a strategy (change add-strategy-backtest, design.md, decision 1): bot or idea. The migration does not fill
    // it: a strategy saved before reads with the kind of its graph, and any write of the strategy writes the kind.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE strategies
                ADD COLUMN kind text NULL,
                ADD CONSTRAINT strategies_kind_ck CHECK (kind IN ('bot', 'idea'));
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE strategies DROP COLUMN kind;");
    }
}
