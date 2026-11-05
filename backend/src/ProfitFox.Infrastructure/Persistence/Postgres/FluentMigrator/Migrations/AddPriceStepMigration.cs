using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070003)]
public sealed class AddPriceStepMigration : ForwardOnlyMigration
{
    // The price step of a pair, which the next listing refresh fills in, and the step an idea goes by, so a target that follows the entry
    // lands on it.
    public override void Up()
    {
        Execute.Sql("""
            ALTER TABLE scanner_pairs ADD COLUMN tick_size numeric NULL;
            ALTER TABLE ideas ADD COLUMN tick numeric NULL;
            """);
    }
}