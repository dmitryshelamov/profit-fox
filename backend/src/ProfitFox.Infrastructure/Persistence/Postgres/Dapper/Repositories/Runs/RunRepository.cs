using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs;

/// <summary>
///     The queue of runs in the table <c>runs</c> (design add-bench-service, decision 3). A Worker takes a run with one
///     <c>UPDATE … FOR UPDATE SKIP LOCKED</c>, so no two Workers take the same run, and every time is the time of the database.
/// </summary>
internal sealed class RunRepository(NpgsqlDataSource database) : IRunRepository
{
    private const string UniqueViolation = "23505";

    public async Task<Run> EnqueueAsync(string id, string hypothesis, int version, string period, bool parity, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        string kind = parity ? "parity" : "verdict";
        // A run that waits or goes can end between the refusal and the look at who stood in the way, and then the next try goes through.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                RunDb row = await connection.QuerySingleAsync<RunDb>(RunRepositoryQueries.Enqueue(id, hypothesis, version, period, kind, cancellationToken));
                return RunDbConverter.ToDomain(row);
            }
            catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
            {
                RunDb? blocking = await connection.QueryFirstOrDefaultAsync<RunDb>(RunRepositoryQueries.Blocking(hypothesis, version, period, kind, cancellationToken));
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

    public async Task<Run?> ClaimAsync(string worker, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        RunDb? row = await connection.QuerySingleOrDefaultAsync<RunDb>(RunRepositoryQueries.Claim(worker, lease, cancellationToken));
        return row is null ? null : RunDbConverter.ToDomain(row);
    }

    public async Task<Renewal?> RenewAsync(string runId, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        bool? cancelRequested = await connection.QuerySingleOrDefaultAsync<bool?>(RunRepositoryQueries.Renew(runId, worker, lease, pairsDone, pairsTotal, cancellationToken));
        return cancelRequested is { } requested ? new Renewal(requested) : null;
    }

    public async Task<CancelOutcome> CancelAsync(string runId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        // A waiting run that a Worker takes between the two statements is going by the second one.
        int? removed = await connection.QuerySingleOrDefaultAsync<int?>(RunRepositoryQueries.RemoveWaiting(runId, cancellationToken));
        if (removed is not null)
        {
            return CancelOutcome.Removed;
        }

        string? status = await connection.QuerySingleOrDefaultAsync<string>(RunRepositoryQueries.RequestCancel(runId, cancellationToken));
        return status switch
        {
            RunState.Running => CancelOutcome.Requested,
            RunState.Finished => CancelOutcome.AlreadyFinished,
            _ => CancelOutcome.NotFound
        };
    }

    public async Task<IReadOnlyList<Run>> ActiveAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<RunDb> rows = await connection.QueryAsync<RunDb>(RunRepositoryQueries.Active(cancellationToken));
        return [.. rows.Select(RunDbConverter.ToDomain)];
    }

    public async Task<Run?> FindAsync(string runId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        RunDb? row = await connection.QuerySingleOrDefaultAsync<RunDb>(RunRepositoryQueries.Find(runId, cancellationToken));
        return row is null ? null : RunDbConverter.ToDomain(row);
    }
}
