using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070004)]
public sealed class HonestIdeaProbabilityMigration : ForwardOnlyMigration
{
    // Decision 3: the buckets are derived data, so they are cleared, not migrated; the scanner computes them again right after it starts.
    // Decision 5: «выше базовой» of a saved filter becomes «ожидание надёжно > 0» in its place among the conditions.
    // Decision 7: the reports of the calibration, read whole, so a report is one jsonb value with numbers as decimal strings.
    public override void Up()
    {
        Execute.Sql("""
            DELETE FROM idea_buckets;
            ALTER TABLE idea_buckets
                ADD COLUMN targets integer NOT NULL,
                ADD COLUMN stops integer NOT NULL,
                ADD COLUMN expectancy_low numeric NULL,
                ADD COLUMN expectancy_high numeric NULL,
                DROP COLUMN baseline;

            UPDATE saved_filters SET conditions = (
                SELECT jsonb_agg(CASE WHEN c->>'operator' = 'above_baseline'
                    THEN jsonb_build_object('field', 'expectancy', 'operator', 'reliably_positive', 'value', null, 'to', null)
                    ELSE c END ORDER BY n)
                FROM jsonb_array_elements(conditions) WITH ORDINALITY AS e(c, n))
            WHERE conditions @> '[{"operator": "above_baseline"}]';

            CREATE TABLE idea_calibration (
                engine_version integer NOT NULL,
                days integer NOT NULL,
                category text NOT NULL,
                computed_at timestamp with time zone NOT NULL,
                report jsonb NOT NULL,
                CONSTRAINT "PK_idea_calibration" PRIMARY KEY (engine_version, days, category)
            );
            """);
    }
}