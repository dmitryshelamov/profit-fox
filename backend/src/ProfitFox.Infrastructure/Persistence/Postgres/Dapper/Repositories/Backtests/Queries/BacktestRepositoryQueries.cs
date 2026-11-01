using System.Data;

using Dapper;

using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;

internal static class BacktestRepositoryQueries
{
    private const string SelectSql = """
                                     SELECT id AS "Id", bot_id AS "BotId", strategy_id AS "StrategyId", strategy_version AS "StrategyVersion", category AS "Category", symbol AS "Symbol", "interval" AS "Interval", "from" AS "From", "to" AS "To", parameters::text AS "Parameters", balance AS "Balance", limit_fee_percent AS "LimitFeePercent", market_fee_percent AS "MarketFeePercent", slippage_percent AS "SlippagePercent", limit_fill_mode AS "LimitFillMode", tick_size AS "TickSize", quantity_step AS "QuantityStep", min_quantity AS "MinQuantity", min_amount AS "MinAmount", search_mode AS "SearchMode", status AS "Status", error AS "Error", import_job_id AS "ImportJobId", actual_start AS "ActualStart", gaps::text AS "Gaps", loaded_candles AS "LoadedCandles", expected_candles AS "ExpectedCandles", calculated_combinations AS "CalculatedCombinations", total_combinations AS "TotalCombinations", speed AS "Speed", stalled_restarts AS "StalledRestarts", created_at AS "CreatedAt", started_at AS "StartedAt", finished_at AS "FinishedAt"
                                     FROM backtests
                                     """;

    private const string InsertSql = """
                                     INSERT INTO backtests (id, bot_id, strategy_id, strategy_version, category, symbol, "interval", "from", "to", parameters, balance, limit_fee_percent, market_fee_percent, slippage_percent, limit_fill_mode, tick_size, quantity_step, min_quantity, min_amount, search_mode, status, error, import_job_id, actual_start, gaps, loaded_candles, expected_candles, calculated_combinations, total_combinations, speed, stalled_restarts, created_at, started_at, finished_at)
                                     VALUES (@Id, @BotId, @StrategyId, @StrategyVersion, @Category, @Symbol, @Interval, @From, @To, @Parameters::jsonb, @Balance, @LimitFeePercent, @MarketFeePercent, @SlippagePercent, @LimitFillMode, @TickSize, @QuantityStep, @MinQuantity, @MinAmount, @SearchMode, @Status, @Error, @ImportJobId, @ActualStart, @Gaps::jsonb, @LoadedCandles, @ExpectedCandles, @CalculatedCombinations, @TotalCombinations, @Speed, @StalledRestarts, @CreatedAt, @StartedAt, @FinishedAt)
                                     """;

    // The status check and the write are one statement, so a concurrent cancel or deletion is never overwritten.
    private const string UpdateSql = """
                                     UPDATE backtests
                                     SET status = @Status, error = @Error, import_job_id = @ImportJobId, actual_start = @ActualStart, gaps = @Gaps::jsonb, loaded_candles = @LoadedCandles, expected_candles = @ExpectedCandles, calculated_combinations = @CalculatedCombinations, total_combinations = @TotalCombinations, speed = @Speed, stalled_restarts = @StalledRestarts, started_at = @StartedAt, finished_at = @FinishedAt
                                     WHERE id = @Id AND status = @Expected
                                     """;

    private const string SelectResultsSql = """
                                            SELECT backtest_id AS "BacktestId", combination AS "Combination", parameter_values::text AS "ParameterValues", start_equity AS "StartEquity", end_equity AS "EndEquity", pnl AS "Pnl", realized_pnl AS "RealizedPnl", unrealized_pnl AS "UnrealizedPnl", return_percent AS "ReturnPercent", max_drawdown_percent AS "MaxDrawdownPercent", return_to_drawdown AS "ReturnToDrawdown", closed_trades AS "ClosedTrades", win_rate_percent AS "WinRatePercent", fills AS "Fills", fees AS "Fees", max_position_cost AS "MaxPositionCost", max_position_cost_percent AS "MaxPositionCostPercent", max_capital_used AS "MaxCapitalUsed", time_in_position_percent AS "TimeInPositionPercent", time_under_water_percent AS "TimeUnderWaterPercent", longest_under_water_candles AS "LongestUnderWaterCandles", skipped_orders AS "SkippedOrders", candles AS "Candles", calculation_time AS "CalculationTime"
                                            FROM backtest_results
                                            """;

    // A result computed again after a restart is the same, so the stored one stays.
    private const string InsertResultSql = """
                                           INSERT INTO backtest_results (backtest_id, combination, parameter_values, start_equity, end_equity, pnl, realized_pnl, unrealized_pnl, return_percent, max_drawdown_percent, return_to_drawdown, closed_trades, win_rate_percent, fills, fees, max_position_cost, max_position_cost_percent, max_capital_used, time_in_position_percent, time_under_water_percent, longest_under_water_candles, skipped_orders, candles, calculation_time)
                                           VALUES (@BacktestId, @Combination, @ParameterValues::jsonb, @StartEquity, @EndEquity, @Pnl, @RealizedPnl, @UnrealizedPnl, @ReturnPercent, @MaxDrawdownPercent, @ReturnToDrawdown, @ClosedTrades, @WinRatePercent, @Fills, @Fees, @MaxPositionCost, @MaxPositionCostPercent, @MaxCapitalUsed, @TimeInPositionPercent, @TimeUnderWaterPercent, @LongestUnderWaterCandles, @SkippedOrders, @Candles, @CalculationTime)
                                           ON CONFLICT (backtest_id, combination) DO NOTHING
                                           """;

    internal static CommandDefinition Insert(BacktestDb backtest, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, backtest, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectSql} WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetByBot(Guid botId, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectSql} WHERE bot_id = @BotId ORDER BY created_at DESC, id DESC", new { BotId = botId }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetUnfinished(CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"{SelectSql} WHERE status IN (@Pending, @Loading, @Running) ORDER BY created_at, id",
            new { Pending = nameof(BacktestStatus.Pending), Loading = nameof(BacktestStatus.LoadingCandles), Running = nameof(BacktestStatus.Running) },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Update(BacktestDb backtest, string expected, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(backtest);
        parameters.Add("Expected", expected);

        return new CommandDefinition(UpdateSql, parameters, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition InsertResult(BacktestResultDb result, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertResultSql, result, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition CalculatedCombinations(Guid backtestId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "SELECT combination FROM backtest_results WHERE backtest_id = @BacktestId ORDER BY combination",
            new { BacktestId = backtestId },
            cancellationToken: cancellationToken);
    }

    // Columns by metric: only these names ever reach the SQL text.
    private static readonly Dictionary<ResultMetric, string> Columns = new Dictionary<ResultMetric, string>
    {
        [ResultMetric.Combination] = "combination",
        [ResultMetric.CalculationTime] = "calculation_time",
        [ResultMetric.StartEquity] = "start_equity",
        [ResultMetric.EndEquity] = "end_equity",
        [ResultMetric.Pnl] = "pnl",
        [ResultMetric.RealizedPnl] = "realized_pnl",
        [ResultMetric.UnrealizedPnl] = "unrealized_pnl",
        [ResultMetric.Return] = "return_percent",
        [ResultMetric.MaxDrawdown] = "max_drawdown_percent",
        [ResultMetric.ReturnToDrawdown] = "return_to_drawdown",
        [ResultMetric.ClosedTrades] = "closed_trades",
        [ResultMetric.WinRate] = "win_rate_percent",
        [ResultMetric.Fills] = "fills",
        [ResultMetric.Fees] = "fees",
        [ResultMetric.MaxPositionCost] = "max_position_cost",
        [ResultMetric.MaxPositionCostPercent] = "max_position_cost_percent",
        [ResultMetric.MaxCapitalUsed] = "max_capital_used",
        [ResultMetric.TimeInPosition] = "time_in_position_percent",
        [ResultMetric.TimeUnderWater] = "time_under_water_percent",
        [ResultMetric.LongestUnderWater] = "longest_under_water_candles",
        [ResultMetric.SkippedOrders] = "skipped_orders",
        [ResultMetric.Candles] = "candles"
    };

    internal static CommandDefinition Results(Guid backtestId, ResultMetric sort, bool descending, int offset, int limit, CancellationToken cancellationToken)
    {
        string order = descending ? "DESC" : "ASC";

        return new CommandDefinition(
            $"{SelectResultsSql} WHERE backtest_id = @BacktestId ORDER BY {Columns[sort]} {order} NULLS LAST, combination OFFSET @Offset LIMIT @Limit",
            new { BacktestId = backtestId, Offset = offset, Limit = limit },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition CountResults(Guid backtestId, CancellationToken cancellationToken)
    {
        return new CommandDefinition("SELECT count(*) FROM backtest_results WHERE backtest_id = @BacktestId", new { BacktestId = backtestId }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Result(Guid backtestId, long combination, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"{SelectResultsSql} WHERE backtest_id = @BacktestId AND combination = @Combination",
            new { BacktestId = backtestId, Combination = combination },
            cancellationToken: cancellationToken);
    }
}