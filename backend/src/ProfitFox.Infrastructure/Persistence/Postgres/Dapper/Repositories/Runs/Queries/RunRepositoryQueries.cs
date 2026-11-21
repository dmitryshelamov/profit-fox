using Dapper;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Queries;

/// <summary>
///     The statements on <c>runs</c>: each one does its whole step by itself, with the time of the database, so Workers on
///     different machines agree.
/// </summary>
internal static class RunRepositoryQueries
{
    private const string Columns =
        """
        id AS "Id", hypothesis_id AS "Hypothesis", version AS "Version", period AS "Period", kind = 'parity' AS "Parity",
        status AS "Status", queued_at AS "QueuedAt", started_at AS "StartedAt", finished_at AS "FinishedAt",
        lease_owner AS "LeaseOwner", leased_until AS "LeasedUntil", lost_leases AS "LostLeases",
        cancel_requested AS "CancelRequested", pairs_done AS "PairsDone", pairs_total AS "PairsTotal"
        """;

    private const string EnqueueSql =
        $"""
         INSERT INTO runs (id, hypothesis_id, version, period, kind, status)
         VALUES (@Id, @Hypothesis, @Version, @Period, @Kind, 'queued')
         RETURNING {Columns}
         """;

    private const string BlockingSql =
        $"""
         SELECT {Columns} FROM runs
         WHERE hypothesis_id = @Hypothesis AND version = @Version AND period = @Period AND kind = @Kind AND status <> 'finished'
         """;

    private const string ClaimSql =
        $"""
         UPDATE runs
         SET status = 'running', lease_owner = @Worker, leased_until = now() + @Lease,
             lost_leases = lost_leases + CASE WHEN status = 'running' THEN 1 ELSE 0 END,
             started_at = now(), pairs_done = 0, pairs_total = NULL
         WHERE id = (SELECT id FROM runs
                     WHERE status = 'queued' OR (status = 'running' AND leased_until < now())
                     ORDER BY queued_at, id
                     LIMIT 1
                     FOR UPDATE SKIP LOCKED)
         RETURNING {Columns}
         """;

    private const string RenewSql =
        """
        UPDATE runs SET leased_until = now() + @Lease, pairs_done = @PairsDone, pairs_total = @PairsTotal
        WHERE id = @RunId AND lease_owner = @Worker AND status = 'running'
        RETURNING cancel_requested
        """;

    private const string RemoveWaitingSql = "DELETE FROM runs WHERE id = @RunId AND status = 'queued' RETURNING 1";

    private const string RequestCancelSql =
        """
        WITH changed AS (UPDATE runs SET cancel_requested = true WHERE id = @RunId AND status = 'running' RETURNING status)
        SELECT status FROM changed
        UNION ALL
        SELECT status FROM runs WHERE id = @RunId AND NOT EXISTS (SELECT 1 FROM changed)
        """;

    private const string ActiveSql = $"SELECT {Columns} FROM runs WHERE status <> 'finished' ORDER BY queued_at, id";

    private const string FindSql = $"SELECT {Columns} FROM runs WHERE id = @RunId";

    public static CommandDefinition Enqueue(string id, string hypothesis, int version, string period, string kind, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            EnqueueSql,
            new { Id = id, Hypothesis = hypothesis, Version = version, Period = period, Kind = kind },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The run of the same version, period and kind that waits or goes.
    /// </summary>
    public static CommandDefinition Blocking(string hypothesis, int version, string period, string kind, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            BlockingSql,
            new { Hypothesis = hypothesis, Version = version, Period = period, Kind = kind },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Claim(string worker, TimeSpan lease, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ClaimSql, new { Worker = worker, Lease = lease }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Renew(string runId, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            RenewSql,
            new { RunId = runId, Worker = worker, Lease = lease, PairsDone = pairsDone, PairsTotal = pairsTotal },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition RemoveWaiting(string runId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RemoveWaitingSql, new { RunId = runId }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Asks the Worker of a going run to stop; gives the status of the run, whatever it is.
    /// </summary>
    public static CommandDefinition RequestCancel(string runId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RequestCancelSql, new { RunId = runId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Active(CancellationToken cancellationToken)
    {
        return new CommandDefinition(ActiveSql, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Find(string runId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(FindSql, new { RunId = runId }, cancellationToken: cancellationToken);
    }
}
