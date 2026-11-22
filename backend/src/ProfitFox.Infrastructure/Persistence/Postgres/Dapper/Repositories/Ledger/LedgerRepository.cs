using Dapper;

using Npgsql;

using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;

/// <summary>
///     The ledger of runs in the tables <c>ledger</c> and <c>reports</c> (spec hypothesis-bench «Журнал прогонов»): rows
///     that are only ever added, which the database itself refuses to change. A decision takes one lock for the whole bench,
///     so the decisions go one at a time. A run of the queue ends in the same step.
/// </summary>
internal sealed class LedgerRepository(NpgsqlDataSource database) : ILedgerRepository
{
    public async Task<IReadOnlyList<LedgerEntry>> EntriesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await Read(connection, transaction: null, cancellationToken);
    }

    public async Task<IReadOnlyList<FinishedRun>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<FinishedRunDb> rows = await connection.QueryAsync<FinishedRunDb>(LedgerRepositoryQueries.Recent(limit, cancellationToken));
        return [.. rows.Select(FinishedRunDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<FinishedRun>> LastAsync(IReadOnlyCollection<HypothesisVersion> versions, CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
        {
            return [];
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<FinishedRunDb> rows = await connection.QueryAsync<FinishedRunDb>(LedgerRepositoryQueries.Last(versions, cancellationToken));
        return [.. rows.Select(FinishedRunDbConverter.ToDomain)];
    }

    public async Task<LedgerResult?> FindAsync(string runId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        LedgerEntryDb? row = await connection.QuerySingleOrDefaultAsync<LedgerEntryDb>(LedgerRepositoryQueries.Find(runId, cancellationToken));
        if (row is null)
        {
            return null;
        }

        string? report = await connection.QuerySingleOrDefaultAsync<string>(LedgerRepositoryQueries.Report(runId, cancellationToken));
        return new LedgerResult(LedgerEntryDbConverter.ToDomain(row), report);
    }

    public Task<LedgerEntry> DecideAsync(Func<IReadOnlyList<LedgerEntry>, Decision> decide, CancellationToken cancellationToken)
    {
        return DecideAsync(decide, assignment: null, cancellationToken);
    }

    public async Task<LedgerEntry> DecideAsync(Func<IReadOnlyList<LedgerEntry>, Decision> decide, RunAssignment? assignment, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(LedgerRepositoryQueries.Lock(transaction, cancellationToken));
        if (assignment is not null)
        {
            // The row stays locked to the end of the step, so a renewal that comes now finds the run finished instead of renewed.
            int? held = await connection.QuerySingleOrDefaultAsync<int?>(LedgerRepositoryQueries.Held(assignment, transaction, cancellationToken));
            if (held is null)
            {
                throw new LeaseLostException(assignment.RunId, assignment.Worker);
            }
        }

        Decision decision = decide(await Read(connection, transaction, cancellationToken));
        if (assignment is not null && decision.Entry.RunId != assignment.RunId)
        {
            throw new InvalidOperationException($"The line {decision.Entry.RunId} is not of the run {assignment.RunId}.");
        }

        await connection.ExecuteAsync(LedgerRepositoryQueries.Insert(decision.Entry, transaction, cancellationToken));
        if (decision.Report is { } report)
        {
            await connection.ExecuteAsync(LedgerRepositoryQueries.InsertReport(decision.Entry.RunId, report, transaction, cancellationToken));
        }

        if (assignment is not null)
        {
            await connection.ExecuteAsync(LedgerRepositoryQueries.Finish(assignment, transaction, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return decision.Entry;
    }

    private static async Task<IReadOnlyList<LedgerEntry>> Read(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        IEnumerable<LedgerEntryDb> rows = await connection.QueryAsync<LedgerEntryDb>(LedgerRepositoryQueries.Entries(transaction, cancellationToken));
        return [.. rows.Select(LedgerEntryDbConverter.ToDomain)];
    }
}
