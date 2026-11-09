using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611090003)]
public sealed class AddOwnIdeaNumbersMigration : ForwardOnlyMigration
{
    // Decision 7 of add-own-idea-numbers: the expectancy without costs of a bucket and its interval, and the curve of «Уровни» by the reward to
    // risk, the cases and the targets of a band of a set interval × side × category; the domain makes the multiplier of them. The buckets
    // written before have none until the first recompute; nothing is moved. The image before this one reads and writes neither.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_buckets
                ADD COLUMN gross_expectancy numeric NULL,
                ADD COLUMN gross_expectancy_low numeric NULL,
                ADD COLUMN gross_expectancy_high numeric NULL;

            CREATE TABLE idea_risk_reward_curves (
                engine_version integer NOT NULL,
                "interval" text NOT NULL,
                side text NOT NULL,
                category text NOT NULL,
                band text NOT NULL,
                cases integer NOT NULL,
                targets integer NOT NULL,
                CONSTRAINT "PK_idea_risk_reward_curves" PRIMARY KEY (engine_version, "interval", side, category, band)
            );
            """);
    }
}