using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070008)]
public sealed class ReplaceIdeaTradingRiskWithAmountMigration : ForwardOnlyMigration
{
    // Decision 4: a risk cannot be turned into an amount without a price and a stop, so the saved row gets the default amount of 20.
    public override void Up()
    {
        Execute.Sql("""
            ALTER TABLE idea_trading_settings ADD COLUMN amount numeric NOT NULL DEFAULT 20;
            ALTER TABLE idea_trading_settings ALTER COLUMN amount DROP DEFAULT;
            ALTER TABLE idea_trading_settings DROP COLUMN risk;
            """);
    }
}