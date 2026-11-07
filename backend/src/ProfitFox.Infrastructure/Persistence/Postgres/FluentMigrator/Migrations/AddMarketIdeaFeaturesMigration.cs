using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080004)]
public sealed class AddMarketIdeaFeaturesMigration : ForwardOnlyMigration
{
    // Decisions 3 and 8 of add-market-idea-features: the numbers of the market a case and an idea fired with, none for those written before; the
    // features a trade copied from its idea, none for a trade before them; the conditions of an auto by the features, «любой» for an auto saved
    // before. The image before this one reads and writes none of the new columns.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_cases
                ADD COLUMN pair_change numeric NULL,
                ADD COLUMN btc_change numeric NULL,
                ADD COLUMN funding numeric NULL;

            ALTER TABLE ideas
                ADD COLUMN pair_change numeric NULL,
                ADD COLUMN btc_change numeric NULL,
                ADD COLUMN funding numeric NULL;

            ALTER TABLE idea_trades
                ADD COLUMN day_drop boolean NOT NULL DEFAULT false,
                ADD COLUMN btc_drop boolean NOT NULL DEFAULT false,
                ADD COLUMN high_funding boolean NOT NULL DEFAULT false;

            ALTER TABLE idea_paper_trades
                ADD COLUMN day_drop boolean NOT NULL DEFAULT false,
                ADD COLUMN btc_drop boolean NOT NULL DEFAULT false,
                ADD COLUMN high_funding boolean NOT NULL DEFAULT false;

            ALTER TABLE idea_autos
                ADD COLUMN day_drop text NOT NULL DEFAULT 'any',
                ADD COLUMN btc_drop text NOT NULL DEFAULT 'any',
                ADD COLUMN high_funding text NOT NULL DEFAULT 'any';
            """);
    }
}