using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611220002)]
public sealed class CreateBacktestTablesMigration : Migration
{
    // The backtests of strategies (change add-strategy-backtest, design.md, decision 2). A backtest keeps the strategy by id,
    // name, version and kind without a foreign key and a copy of the expanded graph of the version, so it reads whatever
    // becomes of the strategy; its lease and progress are those of runs. The sets are written with the backtest and get their
    // numbers, and the pairs theirs, in the transaction that ends it as done. Nothing stops two backtests from going together.
    // The tables of hypotheses, runs, the ledger and the reports stay as they are.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE backtests (
                id text NOT NULL,
                strategy_id uuid NOT NULL,
                strategy_name text NOT NULL,
                strategy_version integer NOT NULL,
                kind text NOT NULL,
                graph jsonb NOT NULL,
                settings jsonb NOT NULL,
                sets integer NOT NULL,
                source_id text NULL,
                source_set integer NULL,
                status text NOT NULL,
                queued_at timestamp with time zone NOT NULL DEFAULT now(),
                started_at timestamp with time zone NULL,
                finished_at timestamp with time zone NULL,
                lease_owner text NULL,
                leased_until timestamp with time zone NULL,
                lost_leases integer NOT NULL DEFAULT 0,
                cancel_requested boolean NOT NULL DEFAULT false,
                pairs_done integer NOT NULL DEFAULT 0,
                pairs_total integer NULL,
                engine_version integer NULL,
                data_hash text NULL,
                read_seconds double precision NULL,
                compute_seconds double precision NULL,
                reason text NULL,
                CONSTRAINT backtests_pk PRIMARY KEY (id),
                CONSTRAINT backtests_kind_ck CHECK (kind IN ('bot', 'idea')),
                CONSTRAINT backtests_status_ck CHECK (status IN ('queued', 'running', 'done', 'failed', 'cancelled')),
                CONSTRAINT backtests_source_ck CHECK ((source_id IS NULL) = (source_set IS NULL))
            );

            CREATE INDEX backtests_queue_ix ON backtests (queued_at, id) WHERE status IN ('queued', 'running');

            CREATE INDEX backtests_finished_ix ON backtests (finished_at DESC) WHERE status IN ('done', 'failed', 'cancelled');

            CREATE INDEX backtests_strategy_ix ON backtests (strategy_id);

            CREATE TABLE backtest_sets (
                backtest_id text NOT NULL,
                number integer NOT NULL,
                timeframe text NOT NULL,
                "values" jsonb NOT NULL,
                numbers jsonb NULL,
                CONSTRAINT backtest_sets_pk PRIMARY KEY (backtest_id, number),
                CONSTRAINT backtest_sets_backtest_fk FOREIGN KEY (backtest_id) REFERENCES backtests (id) ON DELETE CASCADE
            );

            CREATE TABLE backtest_set_pairs (
                backtest_id text NOT NULL,
                number integer NOT NULL,
                pair text NOT NULL,
                numbers jsonb NOT NULL,
                CONSTRAINT backtest_set_pairs_pk PRIMARY KEY (backtest_id, number, pair),
                CONSTRAINT backtest_set_pairs_set_fk FOREIGN KEY (backtest_id, number) REFERENCES backtest_sets (backtest_id, number) ON DELETE CASCADE
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE backtest_set_pairs;
            DROP TABLE backtest_sets;
            DROP TABLE backtests;
            """);
    }
}
