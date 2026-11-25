using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611250001)]
public sealed class AddMarketPairKindMigration : Migration
{
    // The kind of a pair of the snapshot (change add-instrument-kind-filter, design.md, decision 3): crypto or stock. The
    // rows already there are crypto until the first write of the snapshot after the start, a minute at most.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE market_pairs
                ADD COLUMN kind text NOT NULL DEFAULT 'crypto',
                ADD CONSTRAINT market_pairs_kind_ck CHECK (kind IN ('crypto', 'stock'));
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE market_pairs DROP COLUMN kind;");
    }
}
