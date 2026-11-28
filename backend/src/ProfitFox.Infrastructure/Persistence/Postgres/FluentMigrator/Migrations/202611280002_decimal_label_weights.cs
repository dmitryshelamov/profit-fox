using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280002)]
public sealed class DecimalLabelWeightsMigration : Migration
{
    // The weights of the labels become numbers from −100 to +100 with two digits after the point, and the score of an idea a
    // number with two digits (change decimal-label-weights, design.md, decision 1): whole weights and scores stay the same
    // numbers. The change of the type of ideas.score writes the table again and builds its indexes again, the index of the
    // lists by score among them; the table is locked meanwhile (risk 1).
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE label_weights
                DROP CONSTRAINT label_weights_long_ck,
                DROP CONSTRAINT label_weights_short_ck,
                ALTER COLUMN long_weight TYPE numeric(5,2) USING long_weight::numeric(5,2),
                ALTER COLUMN short_weight TYPE numeric(5,2) USING short_weight::numeric(5,2),
                ADD CONSTRAINT label_weights_long_ck CHECK (long_weight BETWEEN -100 AND 100),
                ADD CONSTRAINT label_weights_short_ck CHECK (short_weight BETWEEN -100 AND 100);
            """);
        Execute.Sql("ALTER TABLE ideas ALTER COLUMN score TYPE numeric(7,2) USING score::numeric(7,2);");
    }

    // Whole weights of −3 to +3 again: each rounded and held in that range. The score is counted again from them by the
    // expression of 202611280001, so it stays the sum of the weights of the labels; a rounded sum would not be.
    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE label_weights
                DROP CONSTRAINT label_weights_long_ck,
                DROP CONSTRAINT label_weights_short_ck,
                ALTER COLUMN long_weight TYPE smallint USING greatest(-3, least(3, round(long_weight)))::smallint,
                ALTER COLUMN short_weight TYPE smallint USING greatest(-3, least(3, round(short_weight)))::smallint,
                ADD CONSTRAINT label_weights_long_ck CHECK (long_weight BETWEEN -3 AND 3),
                ADD CONSTRAINT label_weights_short_ck CHECK (short_weight BETWEEN -3 AND 3);
            """);
        Execute.Sql("ALTER TABLE ideas ALTER COLUMN score TYPE smallint USING round(score)::smallint;");
        Execute.Sql(
            """
            UPDATE ideas i
            SET score = c.score
            FROM (SELECT o.id, (SELECT COALESCE(sum(CASE WHEN o.side = 'long' THEN w.long_weight ELSE w.short_weight END), 0)::int
                                FROM label_weights w WHERE w.text = ANY(o.labels)) AS score
                  FROM ideas o WHERE EXISTS (SELECT 1 FROM label_weights w WHERE w.text = ANY(o.labels))) c
            WHERE i.id = c.id AND i.score <> c.score;
            """);
    }
}
