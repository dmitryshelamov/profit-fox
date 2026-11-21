using Dapper;

using Npgsql;

using ProfitFox.Application.Ports.Runs;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs;

/// <summary>
///     The queue of runs in the table <c>runs</c> (design add-bench-service, decision 3). A Worker takes a run with one
///     <c>UPDATE … FOR UPDATE SKIP LOCKED</c>, so no two Workers take the same run, and every time is the time of the database.
/// </summary>
public sealed class RunQueue(NpgsqlDataSource database) : IRunQueue
{
    private const string Columns =
        """
        id AS "Id", hypothesis_id AS "Hypothesis", version AS "Version", period AS "Period", kind = 'parity' AS "Parity",
        status AS "Status", queued_at AS "QueuedAt", started_at AS "StartedAt", finished_at AS "FinishedAt",
        lease_owner AS "LeaseOwner", leased_until AS "LeasedUntil", lost_leases AS "LostLeases",
        cancel_requested AS "CancelRequested", pairs_done AS "PairsDone", pairs_total AS "PairsTotal"
        """;

    private const string UniqueViolation = "23505";

    public async Task<RunRecord> Enqueue(string id, string hypothesis, int version, string period, bool parity, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        string kind = parity ? "parity" : "verdict";
        // A run that waits or goes can end between the refusal and the look at who stood in the way, and then the next try goes through.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                RunRow row = await connection.QuerySingleAsync<RunRow>(new CommandDefinition(
                    $"""
                     INSERT INTO runs (id, hypothesis_id, version, period, kind, status)
                     VALUES (@Id, @Hypothesis, @Version, @Period, @Kind, 'queued')
                     RETURNING {Columns}
                     """,
                    new { Id = id, Hypothesis = hypothesis, Version = version, Period = period, Kind = kind },
                    cancellationToken: cancellation));
                return Record(row);
            }
            catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
            {
                RunRow? blocking = await connection.QueryFirstOrDefaultAsync<RunRow>(new CommandDefinition(
                    $"""
                    SELECT {Columns} FROM runs
                    WHERE hypothesis_id = @Hypothesis AND version = @Version AND period = @Period AND kind = @Kind AND status <> 'finished'
                    """,
                    new { Hypothesis = hypothesis, Version = version, Period = period, Kind = kind },
                    cancellationToken: cancellation));
                if (blocking is not null)
                {
                    string state = blocking.Status == RunState.Queued ? "waits in the queue" : "is going";
                    throw new LedgerException($"{hypothesis} version {version} on {period} already {state} (run {blocking.Id}).", blocking.Id);
                }

                if (attempt == 2)
                {
                    throw;
                }
            }
        }
    }

    public async Task<RunRecord?> Claim(string worker, TimeSpan lease, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        RunRow? row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
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
             """,
            new { Worker = worker, Lease = lease },
            cancellationToken: cancellation));
        return row is null ? null : Record(row);
    }

    public async Task<Renewal?> Renew(string runId, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        bool? cancelRequested = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            """
            UPDATE runs SET leased_until = now() + @Lease, pairs_done = @PairsDone, pairs_total = @PairsTotal
            WHERE id = @RunId AND lease_owner = @Worker AND status = 'running'
            RETURNING cancel_requested
            """,
            new { RunId = runId, Worker = worker, Lease = lease, PairsDone = pairsDone, PairsTotal = pairsTotal },
            cancellationToken: cancellation));
        return cancelRequested is { } requested ? new Renewal(requested) : null;
    }

    public async Task<CancelOutcome> Cancel(string runId, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        // A waiting run that a Worker takes between the two statements is going by the second one.
        int? removed = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "DELETE FROM runs WHERE id = @RunId AND status = 'queued' RETURNING 1", new { RunId = runId }, cancellationToken: cancellation));
        if (removed is not null)
        {
            return CancelOutcome.Removed;
        }

        string? status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            WITH changed AS (UPDATE runs SET cancel_requested = true WHERE id = @RunId AND status = 'running' RETURNING status)
            SELECT status FROM changed
            UNION ALL
            SELECT status FROM runs WHERE id = @RunId AND NOT EXISTS (SELECT 1 FROM changed)
            """,
            new { RunId = runId },
            cancellationToken: cancellation));
        return status switch
        {
            RunState.Running => CancelOutcome.Requested,
            RunState.Finished => CancelOutcome.AlreadyFinished,
            _ => CancelOutcome.NotFound
        };
    }

    public async Task<IReadOnlyList<RunRecord>> Active(CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        IEnumerable<RunRow> rows = await connection.QueryAsync<RunRow>(new CommandDefinition(
            $"SELECT {Columns} FROM runs WHERE status <> 'finished' ORDER BY queued_at, id",
            cancellationToken: cancellation));
        return [.. rows.Select(Record)];
    }

    public async Task<RunRecord?> Find(string runId, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        RunRow? row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
            $"SELECT {Columns} FROM runs WHERE id = @RunId",
            new { RunId = runId },
            cancellationToken: cancellation));
        return row is null ? null : Record(row);
    }

    private static RunRecord Record(RunRow row)
    {
        return new RunRecord(
            row.Id,
            row.Hypothesis,
            row.Version,
            row.Period,
            row.Parity,
            row.Status,
            row.QueuedAt,
            row.StartedAt,
            row.FinishedAt,
            row.LeaseOwner,
            row.LeasedUntil,
            row.LostLeases,
            row.CancelRequested,
            row.PairsDone,
            row.PairsTotal);
    }
}
