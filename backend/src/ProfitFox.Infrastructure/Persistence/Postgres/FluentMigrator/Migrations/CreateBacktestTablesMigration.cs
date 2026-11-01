using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611020003)]
public sealed class CreateBacktestTablesMigration : ForwardOnlyMigration
{
    // A bot takes its backtests and their results along; a backtest keeps the version it ran on, which outlives it.
    // Metrics are columns, so results sort by any of them.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE backtests (
                id uuid NOT NULL,
                bot_id uuid NOT NULL,
                strategy_id uuid NOT NULL,
                strategy_version integer NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                "interval" text NOT NULL,
                "from" timestamp with time zone NOT NULL,
                "to" timestamp with time zone NOT NULL,
                parameters jsonb NOT NULL,
                balance numeric NOT NULL,
                limit_fee_percent numeric NOT NULL,
                market_fee_percent numeric NOT NULL,
                slippage_percent numeric NOT NULL,
                limit_fill_mode text NOT NULL,
                tick_size numeric NOT NULL,
                quantity_step numeric NOT NULL,
                min_quantity numeric NOT NULL,
                min_amount numeric NOT NULL,
                search_mode text NOT NULL,
                status text NOT NULL,
                error text,
                import_job_id uuid,
                actual_start timestamp with time zone,
                gaps jsonb NOT NULL,
                loaded_candles bigint NOT NULL,
                expected_candles bigint NOT NULL,
                calculated_combinations bigint NOT NULL,
                total_combinations bigint NOT NULL,
                speed numeric,
                stalled_restarts integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                started_at timestamp with time zone,
                finished_at timestamp with time zone,
                CONSTRAINT "PK_backtests" PRIMARY KEY (id),
                CONSTRAINT "FK_backtests_bots" FOREIGN KEY (bot_id) REFERENCES bots (id) ON DELETE CASCADE,
                CONSTRAINT "FK_backtests_strategy_versions" FOREIGN KEY (strategy_id, strategy_version) REFERENCES strategy_versions (strategy_id, number)
            );

            CREATE INDEX "IX_backtests_bot_id" ON backtests (bot_id);

            CREATE TABLE backtest_results (
                backtest_id uuid NOT NULL,
                combination bigint NOT NULL,
                parameter_values jsonb NOT NULL,
                start_equity numeric NOT NULL,
                end_equity numeric NOT NULL,
                pnl numeric NOT NULL,
                realized_pnl numeric NOT NULL,
                unrealized_pnl numeric NOT NULL,
                return_percent numeric NOT NULL,
                max_drawdown_percent numeric NOT NULL,
                return_to_drawdown numeric,
                closed_trades integer NOT NULL,
                win_rate_percent numeric,
                fills integer NOT NULL,
                fees numeric NOT NULL,
                max_position_cost numeric NOT NULL,
                max_position_cost_percent numeric NOT NULL,
                max_capital_used numeric NOT NULL,
                time_in_position_percent numeric NOT NULL,
                time_under_water_percent numeric NOT NULL,
                longest_under_water_candles integer NOT NULL,
                skipped_orders bigint NOT NULL,
                candles integer NOT NULL,
                calculation_time interval NOT NULL,
                CONSTRAINT "PK_backtest_results" PRIMARY KEY (backtest_id, combination),
                CONSTRAINT "FK_backtest_results_backtests" FOREIGN KEY (backtest_id) REFERENCES backtests (id) ON DELETE CASCADE
            );
            """);
    }
}