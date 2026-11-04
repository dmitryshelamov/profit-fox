using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611060003)]
public sealed class CreateOpenInterestAndFundingTablesMigration : ForwardOnlyMigration
{
    // Open interest every 5 minutes of every linear pair grows like candles; funding settles a few times a day, so a plain table.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE open_interest (
                category text NOT NULL,
                symbol text NOT NULL,
                "time" timestamp with time zone NOT NULL,
                value numeric NOT NULL,
                CONSTRAINT "PK_open_interest" PRIMARY KEY (category, symbol, "time")
            );

            SELECT create_hypertable('open_interest', by_range('time'));

            CREATE TABLE funding_rates (
                category text NOT NULL,
                symbol text NOT NULL,
                "time" timestamp with time zone NOT NULL,
                rate numeric NOT NULL,
                CONSTRAINT "PK_funding_rates" PRIMARY KEY (category, symbol, "time")
            );
            """);
    }
}