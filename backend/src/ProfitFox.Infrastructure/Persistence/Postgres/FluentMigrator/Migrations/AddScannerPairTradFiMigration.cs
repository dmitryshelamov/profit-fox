using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070010)]
public sealed class AddScannerPairTradFiMigration : ForwardOnlyMigration
{
    // A pair the list of the exchange has not told about yet is crypto; the scanner sets the flag on its first read of the list.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE scanner_pairs ADD COLUMN tradfi boolean NOT NULL DEFAULT false;");
    }
}