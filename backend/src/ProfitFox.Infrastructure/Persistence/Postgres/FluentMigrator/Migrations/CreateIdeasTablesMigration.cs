using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070001)]
public sealed class CreateIdeasTablesMigration : ForwardOnlyMigration
{
    // Decision 5. The partial unique index keeps one open idea per key; a case is written once per key, fired time and version.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE ideas (
                id uuid NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                setup text NOT NULL,
                type text NOT NULL,
                "interval" text NOT NULL,
                side text NOT NULL,
                fired_at timestamp with time zone NOT NULL,
                entry numeric NOT NULL,
                stop numeric NOT NULL,
                target numeric NOT NULL,
                risk_reward numeric NOT NULL,
                status text NOT NULL,
                status_at timestamp with time zone NOT NULL,
                entered_at timestamp with time zone NULL,
                exit_price numeric NULL,
                move numeric NOT NULL,
                volume_ratio numeric NOT NULL,
                engine_version integer NOT NULL,
                CONSTRAINT "PK_ideas" PRIMARY KEY (id)
            );

            CREATE UNIQUE INDEX "UX_ideas_open" ON ideas (category, symbol, setup, type, "interval", side) WHERE status IN ('awaiting_entry', 'active');
            CREATE INDEX "IX_ideas_status_at" ON ideas (status_at);

            CREATE TABLE idea_cases (
                category text NOT NULL,
                symbol text NOT NULL,
                setup text NOT NULL,
                type text NOT NULL,
                "interval" text NOT NULL,
                side text NOT NULL,
                fired_at timestamp with time zone NOT NULL,
                engine_version integer NOT NULL,
                entry numeric NOT NULL,
                stop numeric NOT NULL,
                target numeric NOT NULL,
                risk_reward numeric NOT NULL,
                status text NOT NULL,
                status_at timestamp with time zone NOT NULL,
                entered_at timestamp with time zone NOT NULL,
                exit_price numeric NOT NULL,
                move numeric NOT NULL,
                volume_ratio numeric NOT NULL,
                result numeric NOT NULL,
                CONSTRAINT "PK_idea_cases" PRIMARY KEY (category, symbol, setup, type, "interval", side, fired_at, engine_version)
            );

            SELECT create_hypertable('idea_cases', by_range('fired_at'));

            CREATE TABLE idea_buckets (
                engine_version integer NOT NULL,
                days integer NOT NULL,
                setup text NOT NULL,
                type text NOT NULL,
                "interval" text NOT NULL,
                side text NOT NULL,
                category text NOT NULL,
                cases integer NOT NULL,
                expired integer NOT NULL,
                probability numeric NULL,
                probability_low numeric NULL,
                probability_high numeric NULL,
                baseline numeric NOT NULL,
                expectancy numeric NOT NULL,
                CONSTRAINT "PK_idea_buckets" PRIMARY KEY (engine_version, days, setup, type, "interval", side, category)
            );

            CREATE TABLE idea_replay (
                category text NOT NULL,
                symbol text NOT NULL,
                engine_version integer NOT NULL,
                replayed_until timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_replay" PRIMARY KEY (category, symbol, engine_version)
            );

            CREATE TABLE idea_volatility (
                category text NOT NULL,
                symbol text NOT NULL,
                "interval" text NOT NULL,
                percentile numeric NOT NULL,
                CONSTRAINT "PK_idea_volatility" PRIMARY KEY (category, symbol, "interval")
            );

            CREATE TABLE saved_filters (
                id uuid NOT NULL,
                name text NOT NULL,
                place text NOT NULL,
                conditions jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_saved_filters" PRIMARY KEY (id)
            );

            ALTER TABLE market_snapshot ADD COLUMN setups jsonb NOT NULL DEFAULT '{}';
            """);
    }
}