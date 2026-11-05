using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070002)]
public sealed class AddIdeaReplayOpenIdeasMigration : ForwardOnlyMigration
{
    // The ideas of the replay still open at the cursor, so the next turn goes on from them as one run would.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE idea_replay ADD COLUMN open_ideas jsonb NOT NULL DEFAULT '[]';");
    }
}