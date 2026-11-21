using Dapper;

using Npgsql;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;

/// <summary>
///     The ledger of runs in the tables <c>ledger</c> and <c>reports</c> (spec hypothesis-bench «Журнал прогонов»): rows
///     that are only ever added, which the database itself refuses to change. A decision takes one lock for the whole bench,
///     so the decisions go one at a time. A run of the queue ends in the same step.
/// </summary>
public sealed class RunLedger(NpgsqlDataSource database) : IRunLedger
{
    // The lock is held to the end of the transaction. Verdicts are rare and a decision is a few means over blocks, so one lock
    // for all periods costs nothing and cannot be forgotten for a period.
    private const long DecisionLock = 0x424E4348;

    public async Task<IReadOnlyList<LedgerEntry>> Entries(CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        return await Read(connection, transaction: null, cancellation);
    }

    public async Task<IReadOnlyList<FinishedRun>> Recent(int limit, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        IEnumerable<FinishedRow> rows = await connection.QueryAsync<FinishedRow>(new CommandDefinition(
            LedgerSql.SelectRecent,
            new { Limit = limit },
            cancellationToken: cancellation));
        return [.. rows.Select(row => new FinishedRun(LedgerSql.Entry(row), row.Parity))];
    }

    public async Task<LedgerResult?> Find(string runId, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        LedgerRow? row = await connection.QuerySingleOrDefaultAsync<LedgerRow>(new CommandDefinition(
            LedgerSql.SelectOne,
            new { RunId = runId },
            cancellationToken: cancellation));
        if (row is null)
        {
            return null;
        }

        string? report = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT text FROM reports WHERE run_id = @RunId",
            new { RunId = runId },
            cancellationToken: cancellation));
        return new LedgerResult(LedgerSql.Entry(row), report);
    }

    public Task<LedgerEntry> Decide(Func<IReadOnlyList<LedgerEntry>, Decision> decide, CancellationToken cancellation)
    {
        return Decide(decide, assignment: null, cancellation);
    }

    public async Task<LedgerEntry> Decide(Func<IReadOnlyList<LedgerEntry>, Decision> decide, RunAssignment? assignment, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellation);
        await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Key)", new { Key = DecisionLock }, transaction, cancellationToken: cancellation));
        if (assignment is not null)
        {
            // The row stays locked to the end of the step, so a renewal that comes now finds the run finished instead of renewed.
            int? held = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "SELECT 1 FROM runs WHERE id = @RunId AND lease_owner = @Worker AND status = 'running' FOR UPDATE",
                new { assignment.RunId, assignment.Worker },
                transaction,
                cancellationToken: cancellation));
            if (held is null)
            {
                throw new LeaseLostException(assignment.RunId, assignment.Worker);
            }
        }

        Decision decision = decide(await Read(connection, transaction, cancellation));
        if (assignment is not null && decision.Entry.RunId != assignment.RunId)
        {
            throw new InvalidOperationException($"The line {decision.Entry.RunId} is not of the run {assignment.RunId}.");
        }

        await connection.ExecuteAsync(new CommandDefinition(LedgerSql.Insert, LedgerSql.Parameters(decision.Entry), transaction, cancellationToken: cancellation));
        if (decision.Report is { } report)
        {
            await connection.ExecuteAsync(new CommandDefinition(LedgerSql.InsertReport, new { decision.Entry.RunId, Text = report }, transaction, cancellationToken: cancellation));
        }

        if (assignment is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE runs SET status = 'finished', finished_at = now() WHERE id = @RunId",
                new { assignment.RunId },
                transaction,
                cancellationToken: cancellation));
        }

        await transaction.CommitAsync(cancellation);
        return decision.Entry;
    }

    private static async Task<IReadOnlyList<LedgerEntry>> Read(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellation)
    {
        IEnumerable<LedgerRow> rows = await connection.QueryAsync<LedgerRow>(new CommandDefinition(LedgerSql.Select, transaction: transaction, cancellationToken: cancellation));
        return [.. rows.Select(LedgerSql.Entry)];
    }
}
