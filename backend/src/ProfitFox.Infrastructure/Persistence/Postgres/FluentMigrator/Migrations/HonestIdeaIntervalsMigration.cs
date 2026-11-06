using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080001)]
public sealed class HonestIdeaIntervalsMigration : ForwardOnlyMigration
{
    // Decision 6: the buckets and the calibration are derived data, so they are cleared, not migrated; the scanner computes them again with
    // its first whole pass. The new columns have defaults, so the image before this one still writes its buckets and reports without them.
    // The start of a version of the rules is written by the scanner when it takes the lease, once per version.
    public override void Up()
    {
        Execute.Sql("""
            DELETE FROM idea_buckets;
            ALTER TABLE idea_buckets
                ADD COLUMN own_probability numeric NULL,
                ADD COLUMN own_probability_low numeric NULL,
                ADD COLUMN own_probability_high numeric NULL,
                ADD COLUMN own_expectancy numeric NOT NULL DEFAULT 0,
                ADD COLUMN own_expectancy_low numeric NULL,
                ADD COLUMN own_expectancy_high numeric NULL,
                ADD COLUMN parent_weight numeric NULL,
                ADD COLUMN few_own_cases boolean NOT NULL DEFAULT false;

            DELETE FROM idea_calibration;
            ALTER TABLE idea_calibration
                ADD COLUMN scope text NOT NULL DEFAULT 'all',
                DROP CONSTRAINT "PK_idea_calibration",
                ADD CONSTRAINT "PK_idea_calibration" PRIMARY KEY (engine_version, days, category, scope);

            CREATE TABLE idea_engine_versions (
                engine_version integer NOT NULL,
                started_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_engine_versions" PRIMARY KEY (engine_version)
            );
            """);
    }
}