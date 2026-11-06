using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070009)]
public sealed class TuneIdeaRulesMigration : ForwardOnlyMigration
{
    // Decision 5: the exit rules applied, one per type × interval; none is the default.
    // Decision 1: a row of idea_cases of version 5 is a signal with its path; its outcome depends on the rule and is read, not stored, so the
    // outcome columns of version 4 become empty for it. They stay, so the image before this one still writes and reads its own rows.
    // Decision 6: an idea keeps the exit rule it fired under; the ideas made before it take the defaults of their type.
    // Decision 4: the cursor of the replay keeps the open paths; the open ideas of version 4 stay for a roll back.
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE idea_exit_rules (
                type text NOT NULL,
                "interval" text NOT NULL,
                life_candles integer NOT NULL,
                target_r numeric NULL,
                entry_candles integer NULL,
                applied_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_exit_rules" PRIMARY KEY (type, "interval")
            );

            ALTER TABLE idea_cases
                ALTER COLUMN target DROP NOT NULL,
                ALTER COLUMN risk_reward DROP NOT NULL,
                ALTER COLUMN status DROP NOT NULL,
                ALTER COLUMN status_at DROP NOT NULL,
                ALTER COLUMN entered_at DROP NOT NULL,
                ALTER COLUMN exit_price DROP NOT NULL,
                ALTER COLUMN result DROP NOT NULL,
                ADD COLUMN tick numeric NULL,
                ADD COLUMN entry_after integer NULL,
                ADD COLUMN stop_before_entry_after integer NULL,
                ADD COLUMN stop_after integer NULL,
                ADD COLUMN ladder_after integer[] NULL,
                ADD COLUMN target_after integer NULL,
                ADD COLUMN closes numeric[] NULL,
                ADD COLUMN tracked_until timestamp with time zone NULL;

            ALTER TABLE ideas
                ADD COLUMN life_candles integer NOT NULL DEFAULT 24,
                ADD COLUMN target_r numeric NULL,
                ADD COLUMN entry_candles integer NULL;
            UPDATE ideas SET target_r = 2 WHERE type <> 'levels';
            UPDATE ideas SET entry_candles = 4 WHERE type = 'pullback';
            UPDATE ideas SET entry_candles = 12 WHERE type = 'stop_entry';

            ALTER TABLE idea_replay ADD COLUMN open_signals jsonb NOT NULL DEFAULT '[]';

            ALTER TABLE idea_buckets ADD COLUMN signals integer NULL;
            """);
    }
}