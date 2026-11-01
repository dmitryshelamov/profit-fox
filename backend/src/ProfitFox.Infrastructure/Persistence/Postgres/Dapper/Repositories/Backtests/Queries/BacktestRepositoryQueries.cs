using System.Data;

using Dapper;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;

internal static class BacktestRepositoryQueries
{
    private const string SelectSql = """
                                     SELECT id AS "Id", bot_id AS "BotId", strategy_id AS "StrategyId", strategy_version AS "StrategyVersion", category AS "Category", symbol AS "Symbol", "interval" AS "Interval", "from" AS "From", "to" AS "To", parameters::text AS "Parameters", balance AS "Balance", limit_fee_percent AS "LimitFeePercent", market_fee_percent AS "MarketFeePercent", slippage_percent AS "SlippagePercent", limit_fill_mode AS "LimitFillMode", tick_size AS "TickSize", quantity_step AS "QuantityStep", min_quantity AS "MinQuantity", min_amount AS "MinAmount", search_mode AS "SearchMode", status AS "Status", error AS "Error", import_job_id AS "ImportJobId", actual_start AS "ActualStart", gaps::text AS "Gaps", loaded_candles AS "LoadedCandles", expected_candles AS "ExpectedCandles", calculated_combinations AS "CalculatedCombinations", total_combinations AS "TotalCombinations", speed AS "Speed", created_at AS "CreatedAt", started_at AS "StartedAt", finished_at AS "FinishedAt", engine_version AS "EngineVersion"
                                     FROM backtests
                                     """;

    private const string InsertSql = """
                                     INSERT INTO backtests (id, bot_id, strategy_id, strategy_version, category, symbol, "interval", "from", "to", parameters, balance, limit_fee_percent, market_fee_percent, slippage_percent, limit_fill_mode, tick_size, quantity_step, min_quantity, min_amount, search_mode, status, error, import_job_id, actual_start, gaps, loaded_candles, expected_candles, calculated_combinations, total_combinations, speed, created_at, started_at, finished_at, engine_version)
                                     VALUES (@Id, @BotId, @StrategyId, @StrategyVersion, @Category, @Symbol, @Interval, @From, @To, @Parameters::jsonb, @Balance, @LimitFeePercent, @MarketFeePercent, @SlippagePercent, @LimitFillMode, @TickSize, @QuantityStep, @MinQuantity, @MinAmount, @SearchMode, @Status, @Error, @ImportJobId, @ActualStart, @Gaps::jsonb, @LoadedCandles, @ExpectedCandles, @CalculatedCombinations, @TotalCombinations, @Speed, @CreatedAt, @StartedAt, @FinishedAt, @EngineVersion)
                                     """;

    // The status check and the write are one statement, so a concurrent cancel or deletion is never overwritten.
    private const string UpdateSql = """
                                     UPDATE backtests
                                     SET status = @Status, error = @Error, import_job_id = @ImportJobId, actual_start = @ActualStart, gaps = @Gaps::jsonb, loaded_candles = @LoadedCandles, expected_candles = @ExpectedCandles, calculated_combinations = @CalculatedCombinations, total_combinations = @TotalCombinations, speed = @Speed, started_at = @StartedAt, finished_at = @FinishedAt
                                     WHERE id = @Id AND status = @Expected
                                     """;

    // A Worker that saw no job yet adds one only if no other Worker added it meanwhile.
    private const string StartImportSql = $"{UpdateSql} AND import_job_id IS NULL";

    private const string FailSql = """
                                   UPDATE backtests SET status = @Failed, error = @Reason, finished_at = @Now
                                   WHERE id = @Id AND status = ANY(@Expected)
                                   """;

    private const string InsertChunksSql = """
                                           INSERT INTO backtest_chunks (backtest_id, number, first_combination, combinations, done, lost_leases)
                                           SELECT @BacktestId, chunk.number, chunk.first_combination, chunk.combinations, false, 0
                                           FROM unnest(@Numbers, @Firsts, @Counts) AS chunk(number, first_combination, combinations)
                                           """;

    // Skipping locked rows lets Workers claim chunks at once; a lease taken before this claim is seen again on the locked row.
    private const string ClaimChunkSql = """
                                         UPDATE backtest_chunks AS chunk
                                         SET lease_owner = @Owner, leased_until = @LeasedUntil,
                                             lost_leases = chunk.lost_leases + CASE WHEN chunk.lease_owner IS NULL THEN 0 ELSE 1 END
                                         WHERE (chunk.backtest_id, chunk.number) = (
                                             SELECT backtest_id, number
                                             FROM backtest_chunks
                                             WHERE backtest_id = @BacktestId AND NOT done AND (leased_until IS NULL OR leased_until <= @Now)
                                             ORDER BY number
                                             LIMIT 1
                                             FOR UPDATE SKIP LOCKED)
                                         RETURNING chunk.backtest_id AS "BacktestId", chunk.number AS "Number", chunk.first_combination AS "FirstCombination",
                                             chunk.combinations AS "Combinations", chunk.lost_leases AS "LostLeases"
                                         """;

    private const string RenewChunkLeaseSql = """
                                              UPDATE backtest_chunks SET leased_until = @LeasedUntil
                                              WHERE backtest_id = @BacktestId AND number = @Number AND lease_owner = @Owner
                                              """;

    // Locks the backtest first, as a cancel does, so a save either comes before a cancel or sees it.
    private const string LockRunningSql = "SELECT id FROM backtests WHERE id = @BacktestId AND status = @Running FOR UPDATE";

    private const string SaveChunkSql = """
                                        UPDATE backtest_chunks SET speed = @Speed, lost_leases = 0
                                        WHERE backtest_id = @BacktestId AND number = @Number AND lease_owner = @Owner
                                        """;

    private const string CountResultsOfChunkSql = """
                                                  UPDATE backtests
                                                  SET calculated_combinations = calculated_combinations + @Inserted,
                                                      speed = (SELECT sum(speed) FROM backtest_chunks WHERE backtest_id = @BacktestId AND NOT done AND leased_until > @Now)
                                                  WHERE id = @BacktestId
                                                  """;

    private const string CompleteChunkSql = """
                                            UPDATE backtest_chunks SET done = true, lease_owner = NULL, leased_until = NULL, speed = NULL
                                            WHERE backtest_id = @BacktestId AND number = @Number AND lease_owner = @Owner
                                            """;

    // Run after the chunk is done and committed: of two Workers that finish the last chunks at once, the later one sees both done.
    private const string SucceedIfCompleteSql = """
                                                UPDATE backtests SET status = @Succeeded, finished_at = @Now
                                                WHERE id = @Id AND status = @Running AND NOT EXISTS (SELECT 1 FROM backtest_chunks WHERE backtest_id = @Id AND NOT done)
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

    internal static CommandDefinition GetUnfinished(int engineVersion, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"{SelectSql} WHERE status IN (@Pending, @Loading, @Running) AND engine_version = @EngineVersion ORDER BY created_at, id",
            new
            {
                Pending = nameof(BacktestStatus.Pending),
                Loading = nameof(BacktestStatus.LoadingCandles),
                Running = nameof(BacktestStatus.Running),
                EngineVersion = engineVersion
            },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition FailOutdated(int engineVersion, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE backtests SET status = @Failed, error = @Reason, finished_at = @Now
            WHERE status IN (@Pending, @Loading, @Running) AND engine_version < @EngineVersion
            """,
            new
            {
                Failed = nameof(BacktestStatus.Failed),
                Pending = nameof(BacktestStatus.Pending),
                Loading = nameof(BacktestStatus.LoadingCandles),
                Running = nameof(BacktestStatus.Running),
                EngineVersion = engineVersion,
                Reason = reason,
                Now = now
            },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Update(BacktestDb backtest, string expected, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(backtest);
        parameters.Add("Expected", expected);

        return new CommandDefinition(UpdateSql, parameters, transaction, cancellationToken: cancellationToken);
    }

    // Npgsql sends the array as one parameter.
    internal static CommandDefinition UpdateStatus(Guid id, IEnumerable<string> expected, string status, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "UPDATE backtests SET status = @Status, finished_at = @FinishedAt WHERE id = @Id AND status = ANY(@Expected)",
            new { Id = id, Expected = expected.ToArray(), Status = status, FinishedAt = finishedAt },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition StartImport(BacktestDb backtest, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(backtest);
        parameters.Add("Expected", nameof(BacktestStatus.LoadingCandles));

        return new CommandDefinition(StartImportSql, parameters, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Fail(Guid id, IEnumerable<string> expected, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            FailSql,
            new { Id = id, Expected = expected.ToArray(), Failed = nameof(BacktestStatus.Failed), Reason = reason, Now = now },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition InsertChunks(Guid backtestId, IReadOnlyList<BacktestChunk> chunks, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertChunksSql,
            new
            {
                BacktestId = backtestId,
                Numbers = chunks.Select(chunk => chunk.Number).ToArray(),
                Firsts = chunks.Select(chunk => chunk.First).ToArray(),
                Counts = chunks.Select(chunk => chunk.Combinations).ToArray()
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition ClaimChunk(Guid backtestId, Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            ClaimChunkSql,
            new { BacktestId = backtestId, Owner = owner, Now = now, LeasedUntil = leasedUntil },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition RenewChunkLease(BacktestChunk chunk, Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            RenewChunkLeaseSql,
            new { chunk.BacktestId, chunk.Number, Owner = owner, LeasedUntil = leasedUntil },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition LockRunning(Guid backtestId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            LockRunningSql,
            new { BacktestId = backtestId, Running = nameof(BacktestStatus.Running) },
            transaction,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition SaveChunk(BacktestChunk chunk, Guid owner, decimal? speed, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            SaveChunkSql,
            new { chunk.BacktestId, chunk.Number, Owner = owner, Speed = speed },
            transaction,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition CountResultsOfChunk(Guid backtestId, int inserted, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            CountResultsOfChunkSql,
            new { BacktestId = backtestId, Inserted = inserted, Now = now },
            transaction,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition CompleteChunk(BacktestChunk chunk, Guid owner, CancellationToken cancellationToken)
    {
        return new CommandDefinition(CompleteChunkSql, new { chunk.BacktestId, chunk.Number, Owner = owner }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition SucceedIfComplete(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            SucceedIfCompleteSql,
            new { Id = id, Now = now, Succeeded = nameof(BacktestStatus.Succeeded), Running = nameof(BacktestStatus.Running) },
            cancellationToken: cancellationToken);
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