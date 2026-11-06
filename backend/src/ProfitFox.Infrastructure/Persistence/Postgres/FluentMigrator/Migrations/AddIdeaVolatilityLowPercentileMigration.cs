using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070005)]
public sealed class AddIdeaVolatilityLowPercentileMigration : ForwardOnlyMigration
{
    // The 10th percentile of ATR / price beside the 90th, for «Сжатие»; the rows stored before it have none until the next replay.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE idea_volatility ADD COLUMN low_percentile numeric NULL;");
    }
}