using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080002)]
public sealed class AddIdeaAutoBudgetsMigration : ForwardOnlyMigration
{
    // Decision 7: the budget of an auto and the budget of all the autos are none until the owner sets them, so the autos saved before have no budget and
    // the guard is off. The image before this one reads and writes neither column.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_autos ADD COLUMN budget numeric NULL;
            ALTER TABLE idea_trading_settings ADD COLUMN autos_budget numeric NULL;
            """);
    }
}