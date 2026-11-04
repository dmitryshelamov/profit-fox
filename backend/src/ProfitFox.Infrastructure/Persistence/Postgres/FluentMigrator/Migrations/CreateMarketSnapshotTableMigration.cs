using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611060004)]
public sealed class CreateMarketSnapshotTableMigration : ForwardOnlyMigration
{
    // The one snapshot of the market, a row per pair; an empty metric is NULL.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE market_snapshot (
                category text NOT NULL,
                symbol text NOT NULL,
                computed_at timestamp with time zone NOT NULL,
                price numeric NULL,
                change_5m numeric NULL,
                change_15m numeric NULL,
                change_1h numeric NULL,
                change_4h numeric NULL,
                change_24h numeric NULL,
                turnover_1h numeric NULL,
                turnover_24h numeric NULL,
                open_interest numeric NULL,
                open_interest_change_1h numeric NULL,
                open_interest_change_24h numeric NULL,
                funding_rate numeric NULL,
                spread numeric NULL,
                incomplete boolean NOT NULL,
                CONSTRAINT "PK_market_snapshot" PRIMARY KEY (category, symbol)
            );
            """);
    }
}