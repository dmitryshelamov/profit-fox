using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611090003)]
public sealed class AddOwnIdeaNumbersMigration : ForwardOnlyMigration
{
    // Decision 7 of add-own-idea-numbers: the expectancy without costs of a bucket and its interval. The buckets written before have none until
    // the first recompute; nothing is moved. The image before this one reads and writes none of the columns.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_buckets
                ADD COLUMN gross_expectancy numeric NULL,
                ADD COLUMN gross_expectancy_low numeric NULL,
                ADD COLUMN gross_expectancy_high numeric NULL;
            """);
    }
}