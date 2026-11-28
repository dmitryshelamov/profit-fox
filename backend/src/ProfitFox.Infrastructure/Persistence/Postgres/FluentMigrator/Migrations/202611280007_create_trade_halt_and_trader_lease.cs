using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280007)]
public sealed class CreateTradeHaltAndTraderLeaseMigration : Migration
{
    // The emergency stop (change take-idea-trades, design.md, decision 11): one row with the moment it went off — empty while
    // entries are allowed —, the loss and the limit it went off at, and when the ban was last lifted. The lease of the trading
    // Worker (decision 2): one row taken and renewed on the time of the database, as that of the scanner.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE trade_halt (
                id smallint NOT NULL,
                halted_at timestamptz NULL,
                loss numeric NULL,
                limit_value numeric NULL,
                lifted_at timestamptz NULL,
                CONSTRAINT trade_halt_pk PRIMARY KEY (id),
                CONSTRAINT trade_halt_one_ck CHECK (id = 1)
            );

            CREATE TABLE trader_lease (
                name text NOT NULL,
                worker text NOT NULL,
                until timestamptz NOT NULL,
                polled_at timestamptz NULL,
                CONSTRAINT trader_lease_pk PRIMARY KEY (name)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE trader_lease;
            DROP TABLE trade_halt;
            """);
    }
}
