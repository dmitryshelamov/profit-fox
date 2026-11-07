using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080007)]
public sealed class AddBurstAndAutoLimitsMigration : ForwardOnlyMigration
{
    // Decision 5 of add-burst-and-auto-limits: the count of the pairs of the minute of a live idea, none for an idea before it; the burst a
    // trade copied from its idea, none for a trade before it; the condition of an auto by the burst, «любой» for an auto saved before, and its
    // two limits, none for an auto saved before. The cases of the replay count their pairs when read. The image before this one reads and
    // writes none of the new columns.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE ideas ADD COLUMN burst_pairs integer NULL;

            ALTER TABLE idea_trades ADD COLUMN burst boolean NOT NULL DEFAULT false;

            ALTER TABLE idea_paper_trades ADD COLUMN burst boolean NOT NULL DEFAULT false;

            ALTER TABLE idea_autos
                ADD COLUMN burst text NOT NULL DEFAULT 'any',
                ADD COLUMN max_per_minute_per_side integer NULL,
                ADD COLUMN max_open_per_side integer NULL;
            """);
    }
}