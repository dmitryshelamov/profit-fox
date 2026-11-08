using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080006)]
public sealed class FlipLiquidationSidesMigration : ForwardOnlyMigration
{
    // The stream gives the side of the liquidated position ("a Buy update means that a long position has been liquidated"); the code
    // that wrote the rows so far read it as the side of an order, so every stored side is the opposite of the truth. The deploy stops
    // the scanner before the API migrates, so every row at this moment is from that code and is turned around once. Two steps through
    // values no row has, because the unique index over the event would see two rows of one event meet while they swap.
    public override void Up()
    {
        Execute.Sql(
            """
            UPDATE liquidations SET side = CASE side WHEN 'long' THEN 'flipped-short' WHEN 'short' THEN 'flipped-long' ELSE side END;
            UPDATE liquidations SET side = substr(side, 9) WHERE side LIKE 'flipped-%';
            """);
    }
}