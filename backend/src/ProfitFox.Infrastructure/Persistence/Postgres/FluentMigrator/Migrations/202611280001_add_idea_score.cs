using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280001)]
public sealed class AddIdeaScoreMigration : Migration
{
    // The score of an idea is kept in the idea (change add-idea-score, design.md, decision 2, task 8.1): filled here by the
    // expression of ListColumns.ScoreOf as it is on this day — a migration keeps its own text — and from then on written with
    // the idea and counted again for the ideas of a label whose weights change. The index of the labels finds those ideas;
    // the index of the lists by score reads a page of the journal or of the open ideas by score in its order.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE ideas ADD COLUMN score smallint NOT NULL DEFAULT 0;");
        Execute.Sql(
            """
            UPDATE ideas i
            SET score = (SELECT COALESCE(sum(CASE WHEN i.side = 'long' THEN w.long_weight ELSE w.short_weight END), 0)::int
                         FROM label_weights w WHERE w.text = ANY(i.labels))
            WHERE EXISTS (SELECT 1 FROM label_weights w WHERE w.text = ANY(i.labels));
            """);
        Execute.Sql("CREATE INDEX ideas_labels_gin ON ideas USING gin (labels);");
        Execute.Sql("CREATE INDEX ideas_state_score_ix ON ideas (state, score DESC NULLS LAST, moment DESC, id);");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX ideas_state_score_ix;");
        Execute.Sql("DROP INDEX ideas_labels_gin;");
        Execute.Sql("ALTER TABLE ideas DROP COLUMN score;");
    }
}
