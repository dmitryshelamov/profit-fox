using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611030001)]
public sealed class AddWorkerLeasesMigration : ForwardOnlyMigration
{
    // A running job keeps running with no lease, so the first Worker takes it over. A running backtest has no chunks yet: it goes back to
    // loading, where a Worker checks its candles again and splits it; the combinations that have a result are skipped.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE candle_import_jobs RENAME COLUMN stalled_restarts TO lost_leases;
            ALTER TABLE candle_import_jobs ADD COLUMN lease_owner uuid;
            ALTER TABLE candle_import_jobs ADD COLUMN leased_until timestamp with time zone;

            ALTER TABLE backtests DROP COLUMN stalled_restarts;
            UPDATE backtests SET status = 'LoadingCandles' WHERE status = 'Running';

            CREATE TABLE backtest_chunks (
                backtest_id uuid NOT NULL,
                number integer NOT NULL,
                first_combination bigint NOT NULL,
                combinations integer NOT NULL,
                done boolean NOT NULL,
                lease_owner uuid,
                leased_until timestamp with time zone,
                lost_leases integer NOT NULL,
                speed numeric,
                CONSTRAINT "PK_backtest_chunks" PRIMARY KEY (backtest_id, number),
                CONSTRAINT "FK_backtest_chunks_backtests" FOREIGN KEY (backtest_id) REFERENCES backtests (id) ON DELETE CASCADE
            );
            """);
    }
}