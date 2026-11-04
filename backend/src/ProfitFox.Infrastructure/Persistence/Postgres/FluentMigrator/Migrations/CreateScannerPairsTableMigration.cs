using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611060002)]
public sealed class CreateScannerPairsTableMigration : ForwardOnlyMigration
{
    // The pairs of the scanner and the bounds of their stored data; a pair out of trading keeps its row.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scanner_pairs (
                category text NOT NULL,
                symbol text NOT NULL,
                listed boolean NOT NULL,
                history_from timestamp with time zone NULL,
                covered_until timestamp with time zone NULL,
                oi_covered_until timestamp with time zone NULL,
                funding_covered_until timestamp with time zone NULL,
                listed_at timestamp with time zone NULL,
                CONSTRAINT "PK_scanner_pairs" PRIMARY KEY (category, symbol)
            );
            """);
    }
}