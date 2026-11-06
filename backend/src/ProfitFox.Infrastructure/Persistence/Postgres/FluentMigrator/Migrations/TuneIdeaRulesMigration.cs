using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070008)]
public sealed class TuneIdeaRulesMigration : ForwardOnlyMigration
{
    // Decision 6: an idea keeps the exit rule it fired under; the ideas made before it take the defaults of their type.
    public override void Up()
    {
        Execute.Sql("""
            ALTER TABLE ideas
                ADD COLUMN life_candles integer NOT NULL DEFAULT 24,
                ADD COLUMN target_r numeric NULL,
                ADD COLUMN entry_candles integer NULL;
            UPDATE ideas SET target_r = 2 WHERE type <> 'levels';
            UPDATE ideas SET entry_candles = 4 WHERE type = 'pullback';
            UPDATE ideas SET entry_candles = 12 WHERE type = 'stop_entry';
            """);
    }
}