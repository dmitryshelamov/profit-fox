using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611230001)]
public sealed class CreateIdeaTablesMigration : Migration
{
    // The tables of ideas and of the market (change add-ideas-and-market, design.md, decision 14). Whether a strategy gives
    // ideas and the label of a signal go with their strategy and signal; an idea keeps its strategy by id and name without a
    // foreign key, so the journal outlives the strategy, and a strategy has at most one open idea on a pair and timeframe.
    // The snapshot of the market is written whole by the scanner, whose lease is one row.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE strategy_ideas (
                strategy_id uuid NOT NULL,
                enabled boolean NOT NULL,
                timeframes text[] NOT NULL,
                updated_at timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT strategy_ideas_pk PRIMARY KEY (strategy_id),
                CONSTRAINT strategy_ideas_strategy_fk FOREIGN KEY (strategy_id) REFERENCES strategies (id) ON DELETE CASCADE
            );

            CREATE TABLE ideas (
                id uuid NOT NULL,
                strategy_id uuid NOT NULL,
                strategy_name text NOT NULL,
                strategy_version integer NOT NULL,
                timeframe text NOT NULL,
                symbol text NOT NULL,
                side text NOT NULL,
                moment timestamp with time zone NOT NULL,
                size numeric NOT NULL,
                after_minutes integer NOT NULL,
                until timestamp with time zone NOT NULL,
                stop double precision NULL,
                target double precision NULL,
                candle_close numeric NOT NULL,
                labels text[] NOT NULL DEFAULT '{}',
                state text NOT NULL,
                entry_raw double precision NULL,
                entry_price double precision NULL,
                entry_time timestamp with time zone NULL,
                stop_price double precision NULL,
                target_price double precision NULL,
                funding_paid double precision NOT NULL DEFAULT 0,
                last_close double precision NULL,
                computed_until timestamp with time zone NULL,
                now_result double precision NULL,
                now_at timestamp with time zone NULL,
                exit_time timestamp with time zone NULL,
                exit_reason text NULL,
                ambiguous boolean NOT NULL DEFAULT false,
                exit_price double precision NULL,
                result double precision NULL,
                result_usdt double precision NULL,
                excluded text NULL,
                CONSTRAINT ideas_pk PRIMARY KEY (id),
                CONSTRAINT ideas_side_ck CHECK (side IN ('long', 'short')),
                CONSTRAINT ideas_state_ck CHECK (state IN ('open', 'closed', 'excluded')),
                CONSTRAINT ideas_exit_reason_ck CHECK (exit_reason IS NULL OR exit_reason IN ('stop', 'target', 'time'))
            );

            CREATE INDEX ideas_state_moment_ix ON ideas (state, moment DESC);

            CREATE INDEX ideas_symbol_moment_ix ON ideas (symbol, moment DESC);

            CREATE INDEX ideas_strategy_timeframe_ix ON ideas (strategy_id, timeframe);

            CREATE UNIQUE INDEX ideas_one_open_ux ON ideas (strategy_id, symbol, timeframe) WHERE state = 'open';

            CREATE TABLE signal_labels (
                signal_id uuid NOT NULL,
                enabled boolean NOT NULL,
                text text NOT NULL,
                timeframe text NOT NULL,
                CONSTRAINT signal_labels_pk PRIMARY KEY (signal_id),
                CONSTRAINT signal_labels_signal_fk FOREIGN KEY (signal_id) REFERENCES signals (id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX signal_labels_text_ux ON signal_labels (text) WHERE enabled;

            CREATE TABLE market_pairs (
                symbol text NOT NULL,
                price numeric NOT NULL,
                change_1h numeric NULL,
                change_4h numeric NULL,
                change_24h numeric NULL,
                turnover_24h numeric NULL,
                open_interest numeric NULL,
                open_interest_usdt numeric NULL,
                funding_rate numeric NULL,
                next_funding timestamp with time zone NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT market_pairs_pk PRIMARY KEY (symbol)
            );

            CREATE TABLE saved_filters (
                id uuid NOT NULL,
                name text NOT NULL,
                place text NOT NULL,
                conditions jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT saved_filters_pk PRIMARY KEY (id),
                CONSTRAINT saved_filters_name_uk UNIQUE (name),
                CONSTRAINT saved_filters_place_ck CHECK (place IN ('market', 'ideas', 'both'))
            );

            CREATE TABLE scanner_lease (
                name text NOT NULL,
                worker text NOT NULL,
                until timestamp with time zone NOT NULL,
                live_since timestamp with time zone NULL,
                warmed integer NOT NULL DEFAULT 0,
                pairs integer NOT NULL DEFAULT 0,
                CONSTRAINT scanner_lease_pk PRIMARY KEY (name)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE scanner_lease;
            DROP TABLE saved_filters;
            DROP TABLE market_pairs;
            DROP TABLE signal_labels;
            DROP TABLE ideas;
            DROP TABLE strategy_ideas;
            """);
    }
}
