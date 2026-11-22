using System.Data;

using Dapper;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;

/// <summary>
///     The statements on <c>backtests</c>, <c>backtest_sets</c> and <c>backtest_set_pairs</c>: each step of the queue is one
///     statement with the time of the database, as the statements on <c>runs</c> are.
/// </summary>
internal static class BacktestRepositoryQueries
{
    private const string Columns =
        """
        b.id AS "Id", b.strategy_id AS "StrategyId", b.strategy_name AS "StrategyName", b.strategy_version AS "StrategyVersion",
        b.kind AS "Kind", b.graph::text AS "Graph", b.settings::text AS "Settings", b.sets AS "Sets", b.source_id AS "SourceId",
        b.source_set AS "SourceSet", b.status AS "Status", b.queued_at AS "QueuedAt", b.started_at AS "StartedAt",
        b.finished_at AS "FinishedAt", b.lease_owner AS "LeaseOwner", b.leased_until AS "LeasedUntil", b.lost_leases AS "LostLeases",
        b.cancel_requested AS "CancelRequested", b.pairs_done AS "PairsDone", b.pairs_total AS "PairsTotal",
        b.engine_version AS "EngineVersion", b.data_hash AS "DataHash", b.read_seconds AS "ReadSeconds",
        b.compute_seconds AS "ComputeSeconds", b.reason AS "Reason"
        """;

    private const string BestAmount =
        """
        (SELECT max((s.numbers ->> 'amount')::double precision) FROM backtest_sets s WHERE s.backtest_id = b.id) AS "BestAmount"
        """;

    private const string EnqueueSql =
        $"""
         INSERT INTO backtests AS b (id, strategy_id, strategy_name, strategy_version, kind, graph, settings, sets, source_id, source_set, status)
         VALUES (@Id, @StrategyId, @StrategyName, @StrategyVersion, @Kind, @Graph::jsonb, @Settings::jsonb, @Sets, @SourceId, @SourceSet, 'queued')
         RETURNING {Columns}
         """;

    private const string InsertSetSql =
        """
        INSERT INTO backtest_sets (backtest_id, number, timeframe, "values") VALUES (@BacktestId, @Number, @Timeframe, @Values::jsonb)
        """;

    private const string ClaimSql =
        $"""
         UPDATE backtests AS b
         SET status = 'running', lease_owner = @Worker, leased_until = now() + @Lease,
             lost_leases = b.lost_leases + CASE WHEN b.status = 'running' THEN 1 ELSE 0 END,
             started_at = now(), pairs_done = 0, pairs_total = NULL
         WHERE b.id = (SELECT id FROM backtests
                       WHERE status = 'queued' OR (status = 'running' AND leased_until < now())
                       ORDER BY queued_at, id
                       LIMIT 1
                       FOR UPDATE SKIP LOCKED)
         RETURNING {Columns}
         """;

    private const string RenewSql =
        """
        UPDATE backtests SET leased_until = now() + @Lease, pairs_done = @PairsDone, pairs_total = @PairsTotal
        WHERE id = @Id AND lease_owner = @Worker AND status = 'running'
        RETURNING cancel_requested
        """;

    private const string RemoveWaitingSql = "DELETE FROM backtests WHERE id = @Id AND status = 'queued' RETURNING 1";

    private const string RequestCancelSql =
        """
        WITH changed AS (UPDATE backtests SET cancel_requested = true WHERE id = @Id AND status = 'running' RETURNING status)
        SELECT status FROM changed
        UNION ALL
        SELECT status FROM backtests WHERE id = @Id AND NOT EXISTS (SELECT 1 FROM changed)
        """;

    private const string HeldSql = "SELECT 1 FROM backtests WHERE id = @RunId AND lease_owner = @Worker AND status = 'running' FOR UPDATE";

    private const string SetNumbersSql = "UPDATE backtest_sets SET numbers = @Numbers::jsonb WHERE backtest_id = @BacktestId AND number = @Number";

    private const string InsertPairSql =
        "INSERT INTO backtest_set_pairs (backtest_id, number, pair, numbers) VALUES (@BacktestId, @Number, @Pair, @Numbers::jsonb)";

    private const string DoneSql =
        """
        UPDATE backtests
        SET status = 'done', finished_at = now(), engine_version = @EngineVersion, data_hash = @DataHash,
            read_seconds = @ReadSeconds, compute_seconds = @ComputeSeconds, reason = NULL, pairs_done = @Pairs, pairs_total = @Pairs
        WHERE id = @RunId
        """;

    private const string StopSql =
        """
        UPDATE backtests SET status = @Status, finished_at = now(), reason = @Reason
        WHERE id = @RunId AND lease_owner = @Worker AND status = 'running'
        RETURNING 1
        """;

    private const string ActiveSql = $"SELECT {Columns} FROM backtests b WHERE b.status IN ('queued', 'running') ORDER BY b.queued_at, b.id";

    private const string FinishedSql =
        $"""
         SELECT {Columns}, {BestAmount} FROM backtests b
         WHERE b.status IN ('done', 'failed', 'cancelled')
         ORDER BY b.finished_at DESC, b.id
         LIMIT @Limit
         """;

    private const string FindSql = $"SELECT {Columns}, {BestAmount} FROM backtests b WHERE b.id = @Id";

    private const string SetsSql =
        """
        SELECT backtest_id AS "BacktestId", number AS "Number", timeframe AS "Timeframe", "values"::text AS "Values", numbers::text AS "Numbers"
        FROM backtest_sets WHERE backtest_id = @Id ORDER BY number
        """;

    private const string SetExistsSql = "SELECT EXISTS (SELECT 1 FROM backtest_sets WHERE backtest_id = @Id AND number = @Number)";

    private const string PairsSql =
        """
        SELECT backtest_id AS "BacktestId", number AS "Number", pair AS "Pair", numbers::text AS "Numbers"
        FROM backtest_set_pairs WHERE backtest_id = @Id AND number = @Number
        ORDER BY (numbers ->> 'amount')::double precision DESC, pair COLLATE "C"
        """;

    // The first in the queue of the backtests of a strategy that wait or go, otherwise the one that ended last.
    private const string LastSql =
        $"""
         SELECT DISTINCT ON (b.strategy_id) {Columns}, {BestAmount} FROM backtests b
         ORDER BY b.strategy_id,
                  b.status IN ('queued', 'running') DESC,
                  CASE WHEN b.status IN ('queued', 'running') THEN b.queued_at END,
                  b.finished_at DESC NULLS LAST,
                  b.id
         """;

    public static CommandDefinition Enqueue(Backtest backtest, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            EnqueueSql,
            new
            {
                backtest.Id,
                backtest.StrategyId,
                backtest.StrategyName,
                backtest.StrategyVersion,
                Kind = StrategyKinds.Name(backtest.Kind),
                Graph = BacktestDbConverter.WriteGraph(backtest),
                Settings = BacktestDbConverter.WriteSettings(backtest.Settings),
                Sets = backtest.Sets.Count,
                SourceId = backtest.Source?.Backtest,
                SourceSet = backtest.Source?.Set
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertSets(string id, IReadOnlyList<BacktestSetOutcome> sets, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertSetSql,
            sets.Select(set => new { BacktestId = id, set.Set.Number, Timeframe = Timeframes.Name(set.Set.Timeframe), Values = BacktestDbConverter.WriteValues(set.Set) }),
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Claim(string worker, TimeSpan lease, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ClaimSql, new { Worker = worker, Lease = lease }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Renew(string id, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            RenewSql,
            new { Id = id, Worker = worker, Lease = lease, PairsDone = pairsDone, PairsTotal = pairsTotal },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition RemoveWaiting(string id, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RemoveWaitingSql, new { Id = id }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Asks the Worker of a going backtest to stop; gives the status of the backtest, whatever it is.
    /// </summary>
    public static CommandDefinition RequestCancel(string id, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RequestCancelSql, new { Id = id }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The row of a backtest the Worker holds, locked to the end of the transaction.
    /// </summary>
    public static CommandDefinition Held(RunAssignment assignment, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(HeldSql, new { assignment.RunId, assignment.Worker }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition SetNumbers(string id, int set, SetSummary numbers, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            SetNumbersSql,
            new { BacktestId = id, Number = set, Numbers = BacktestDbConverter.WriteNumbers(numbers) },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertPairs(string id, int set, IReadOnlyList<PairSummary> pairs, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertPairSql,
            pairs.Select(pair => new { BacktestId = id, Number = set, pair.Pair, Numbers = BacktestDbConverter.WriteNumbers(pair.Numbers) }),
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Done(RunAssignment assignment, BacktestOutcome outcome, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            DoneSql,
            new { assignment.RunId, outcome.EngineVersion, outcome.DataHash, outcome.ReadSeconds, outcome.ComputeSeconds, outcome.Pairs },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Stop(RunAssignment assignment, string status, string reason, CancellationToken cancellationToken)
    {
        return new CommandDefinition(StopSql, new { assignment.RunId, assignment.Worker, Status = status, Reason = reason }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Active(CancellationToken cancellationToken)
    {
        return new CommandDefinition(ActiveSql, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Finished(int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(FinishedSql, new { Limit = limit }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Find(string id, CancellationToken cancellationToken)
    {
        return new CommandDefinition(FindSql, new { Id = id }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Sets(string id, CancellationToken cancellationToken, IDbTransaction? transaction = null)
    {
        return new CommandDefinition(SetsSql, new { Id = id }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition SetExists(string id, int set, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SetExistsSql, new { Id = id, Number = set }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Pairs(string id, int set, CancellationToken cancellationToken)
    {
        return new CommandDefinition(PairsSql, new { Id = id, Number = set }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Last(CancellationToken cancellationToken)
    {
        return new CommandDefinition(LastSql, cancellationToken: cancellationToken);
    }
}
