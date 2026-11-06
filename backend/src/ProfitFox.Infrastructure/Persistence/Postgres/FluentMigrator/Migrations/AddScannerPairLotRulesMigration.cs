using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070011)]
public sealed class AddScannerPairLotRulesMigration : ForwardOnlyMigration
{
    // Decision 5: the step of the volume and the smallest volume and amount of an order, as the list of instruments tells them; the first
    // reading of the list after this migration fills them, and until then a paper trade of the pair is refused for unknown rules.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE scanner_pairs ADD COLUMN quantity_step numeric NULL, ADD COLUMN min_quantity numeric NULL, ADD COLUMN min_amount numeric NULL;");
    }
}