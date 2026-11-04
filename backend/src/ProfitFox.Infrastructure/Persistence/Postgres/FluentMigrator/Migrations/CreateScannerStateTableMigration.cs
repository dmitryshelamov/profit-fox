using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611060001)]
public sealed class CreateScannerStateTableMigration : ForwardOnlyMigration
{
    // One row for the one scanner that holds the stream: its lease and what the API shows of it.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scanner_state (
                id integer NOT NULL,
                lease_owner uuid NULL,
                leased_until timestamp with time zone NULL,
                snapshot_at timestamp with time zone NULL,
                pairs_total integer NOT NULL,
                pairs_backfilled integer NOT NULL,
                rest_paused_until timestamp with time zone NULL,
                CONSTRAINT "PK_scanner_state" PRIMARY KEY (id),
                CONSTRAINT "CK_scanner_state_one_row" CHECK (id = 1)
            );

            INSERT INTO scanner_state (id, pairs_total, pairs_backfilled) VALUES (1, 0, 0);
            """);
    }
}