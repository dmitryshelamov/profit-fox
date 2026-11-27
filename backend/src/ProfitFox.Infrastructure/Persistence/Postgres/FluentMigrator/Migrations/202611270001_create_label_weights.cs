using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611270001)]
public sealed class CreateLabelWeightsMigration : Migration
{
    // The weights of the labels (change add-idea-score, design.md, decision 1): a row by the text of a label, as an idea keeps
    // its labels by text, with a weight for a long and one for a short of −3 to +3. A text without a row weighs 0.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE label_weights (
                text text NOT NULL,
                long_weight smallint NOT NULL DEFAULT 0,
                short_weight smallint NOT NULL DEFAULT 0,
                CONSTRAINT label_weights_pk PRIMARY KEY (text),
                CONSTRAINT label_weights_long_ck CHECK (long_weight BETWEEN -3 AND 3),
                CONSTRAINT label_weights_short_ck CHECK (short_weight BETWEEN -3 AND 3)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE label_weights;");
    }
}
