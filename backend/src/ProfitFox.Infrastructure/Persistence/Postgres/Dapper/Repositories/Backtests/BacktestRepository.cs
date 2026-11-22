using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;

/// <summary>
///     The backtests in <c>backtests</c>, <c>backtest_sets</c> and <c>backtest_set_pairs</c> (change add-strategy-backtest,
///     design.md, decision 2). The queue is that of <c>runs</c>: a Worker takes a backtest with one
///     <c>UPDATE … FOR UPDATE SKIP LOCKED</c>, and every time is the time of the database. A backtest is put in the queue
///     with its sets in one transaction, and ends as done with the numbers of its sets and pairs in another, while its
///     Worker holds the row: the numbers never come without the end, and a lost lease writes nothing.
/// </summary>
internal sealed class BacktestRepository(NpgsqlDataSource database) : IBacktestRepository
{
    public async Task<Backtest> EnqueueAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        BacktestDb row = await connection.QuerySingleAsync<BacktestDb>(BacktestRepositoryQueries.Enqueue(backtest, transaction, cancellationToken));
        await connection.ExecuteAsync(BacktestRepositoryQueries.InsertSets(backtest.Id, backtest.Sets, transaction, cancellationToken));
        IEnumerable<BacktestSetDb> sets = await connection.QueryAsync<BacktestSetDb>(BacktestRepositoryQueries.Sets(backtest.Id, cancellationToken, transaction));
        await transaction.CommitAsync(cancellationToken);
        return BacktestDbConverter.ToDomain(row, [.. sets]);
    }

    public async Task<Backtest?> ClaimAsync(string worker, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        BacktestDb? row = await connection.QuerySingleOrDefaultAsync<BacktestDb>(BacktestRepositoryQueries.Claim(worker, lease, cancellationToken));
        if (row is null)
        {
            return null;
        }

        IEnumerable<BacktestSetDb> sets = await connection.QueryAsync<BacktestSetDb>(BacktestRepositoryQueries.Sets(row.Id, cancellationToken));
        return BacktestDbConverter.ToDomain(row, [.. sets]);
    }

    public async Task<Renewal?> RenewAsync(string id, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        bool? cancelRequested = await connection.QuerySingleOrDefaultAsync<bool?>(BacktestRepositoryQueries.Renew(id, worker, lease, pairsDone, pairsTotal, cancellationToken));
        return cancelRequested is { } requested ? new Renewal(requested) : null;
    }

    public async Task<CancelOutcome> CancelAsync(string id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        // A waiting backtest that a Worker takes between the two statements is going by the second one.
        int? removed = await connection.QuerySingleOrDefaultAsync<int?>(BacktestRepositoryQueries.RemoveWaiting(id, cancellationToken));
        if (removed is not null)
        {
            return CancelOutcome.Removed;
        }

        string? status = await connection.QuerySingleOrDefaultAsync<string>(BacktestRepositoryQueries.RequestCancel(id, cancellationToken));
        return status switch
        {
            BacktestState.Running => CancelOutcome.Requested,
            BacktestState.Done or BacktestState.Failed or BacktestState.Cancelled => CancelOutcome.AlreadyFinished,
            _ => CancelOutcome.NotFound
        };
    }

    public async Task FinishAsync(RunAssignment assignment, BacktestOutcome outcome, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        // The row stays locked to the end of the step, so a renewal that comes now finds the backtest done instead of renewed.
        if (await connection.QuerySingleOrDefaultAsync<int?>(BacktestRepositoryQueries.Held(assignment, transaction, cancellationToken)) is null)
        {
            throw new LeaseLostException(assignment.RunId, assignment.Worker);
        }

        foreach ((int set, SetSummary numbers, IReadOnlyList<PairSummary> pairs) in outcome.Sets)
        {
            await connection.ExecuteAsync(BacktestRepositoryQueries.SetNumbers(assignment.RunId, set, numbers, transaction, cancellationToken));
            if (pairs.Count > 0)
            {
                await connection.ExecuteAsync(BacktestRepositoryQueries.InsertPairs(assignment.RunId, set, pairs, transaction, cancellationToken));
            }
        }

        await connection.ExecuteAsync(BacktestRepositoryQueries.Done(assignment, outcome, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task StopAsync(RunAssignment assignment, string status, string reason, CancellationToken cancellationToken)
    {
        if (status is not (BacktestState.Failed or BacktestState.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A backtest stops failed or cancelled.");
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        if (await connection.QuerySingleOrDefaultAsync<int?>(BacktestRepositoryQueries.Stop(assignment, status, reason, cancellationToken)) is null)
        {
            throw new LeaseLostException(assignment.RunId, assignment.Worker);
        }
    }

    public async Task<IReadOnlyList<Backtest>> ActiveAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<BacktestDb> rows = await connection.QueryAsync<BacktestDb>(BacktestRepositoryQueries.Active(cancellationToken));
        return [.. rows.Select(row => BacktestDbConverter.ToDomain(row))];
    }

    public async Task<IReadOnlyList<Backtest>> FinishedAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<BacktestDb> rows = await connection.QueryAsync<BacktestDb>(BacktestRepositoryQueries.Finished(limit, cancellationToken));
        return [.. rows.Select(row => BacktestDbConverter.ToDomain(row))];
    }

    public async Task<Backtest?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        BacktestDb? row = await connection.QuerySingleOrDefaultAsync<BacktestDb>(BacktestRepositoryQueries.Find(id, cancellationToken));
        if (row is null)
        {
            return null;
        }

        IEnumerable<BacktestSetDb> sets = await connection.QueryAsync<BacktestSetDb>(BacktestRepositoryQueries.Sets(id, cancellationToken));
        return BacktestDbConverter.ToDomain(row, [.. sets]);
    }

    public async Task<IReadOnlyList<PairSummary>?> PairsAsync(string id, int set, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(BacktestRepositoryQueries.SetExists(id, set, cancellationToken)))
        {
            return null;
        }

        IEnumerable<BacktestPairDb> rows = await connection.QueryAsync<BacktestPairDb>(BacktestRepositoryQueries.Pairs(id, set, cancellationToken));
        return [.. rows.Select(BacktestDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyDictionary<Guid, Backtest>> LastOfStrategiesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<BacktestDb> rows = await connection.QueryAsync<BacktestDb>(BacktestRepositoryQueries.Last(cancellationToken));
        return rows.Select(row => BacktestDbConverter.ToDomain(row)).ToDictionary(backtest => backtest.StrategyId);
    }
}
