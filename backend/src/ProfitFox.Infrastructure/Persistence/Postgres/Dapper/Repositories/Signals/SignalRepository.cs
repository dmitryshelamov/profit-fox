using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals;

/// <summary>
///     The saved signals in the tables <c>signals</c> and <c>signal_versions</c> (spec signal-constructor «Версии
///     сигнала»): a row per signal that changes, and a row per version of its graph that is only added. A save that changes
///     only the layout or the description adds no version, which is told by comparing the graphs as jsonb: by content, not by
///     the order of keys or the spaces. A version a strategy points at keeps the signal from being deleted: the foreign key of
///     <c>strategy_signals</c> refuses it (change add-strategy-constructor, design.md, decision 4).
/// </summary>
internal sealed class SignalRepository(NpgsqlDataSource database) : ISignalRepository
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    public async Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<string>(SignalRepositoryQueries.Names(cancellationToken))];
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(SignalRepositoryQueries.Exists(name, cancellationToken));
    }

    public async Task<SignalRead?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        SignalDb? row = await connection.QuerySingleOrDefaultAsync<SignalDb>(SignalRepositoryQueries.Read(name, cancellationToken));
        return row is null ? null : SignalDbConverter.ToDomain(row);
    }

    public async Task<IReadOnlyList<SignalVersion>> ReadVersionsAsync(IReadOnlyCollection<SignalReference> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return [];
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<SignalVersionDb> rows = await connection.QueryAsync<SignalVersionDb>(SignalRepositoryQueries.Versions(references, cancellationToken));
        return [.. rows.Select(SignalDbConverter.ToDomain)];
    }

    public async Task<SignalDocument> CreateAsync(SignalDocument signal, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid id = signal.Id;
        try
        {
            await connection.ExecuteAsync(SignalRepositoryQueries.Insert(id, signal, transaction, cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw Taken(signal.Name);
        }

        await AddVersion(connection, transaction, id, 1, signal, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SignalDocument.Restore(id, signal.Name, signal.Description, signal.Graph, signal.Layout, version: 1);
    }

    // The row is locked, so two saves of one signal do not take the same next number.
    public async Task<SignalDocument> ReplaceAsync(SignalDocument signal, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        SignalDb? current = await connection.QuerySingleOrDefaultAsync<SignalDb>(SignalRepositoryQueries.Current(signal.Name, transaction, cancellationToken));
        if (current is null)
        {
            throw NotFound(signal.Name);
        }

        string graph = SignalJson.WriteGraph(signal);
        bool same = await connection.ExecuteScalarAsync<bool>(SignalRepositoryQueries.SameGraph(current.Id, current.Version, graph, transaction, cancellationToken));
        int next = same ? current.Version : current.Version + 1;
        if (!same)
        {
            await AddVersion(connection, transaction, current.Id, next, signal, cancellationToken);
        }

        await connection.ExecuteAsync(SignalRepositoryQueries.Update(current.Id, signal, next, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return SignalDocument.Restore(current.Id, signal.Name, signal.Description, signal.Graph, signal.Layout, next);
    }

    public async Task RenameAsync(string name, string to, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            int changed = await connection.ExecuteAsync(SignalRepositoryQueries.Rename(name, to, cancellationToken));
            if (changed == 0)
            {
                throw NotFound(name);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw Taken(to);
        }
    }

    // The delete cascades into the versions and stops at the versions a strategy points at; the names are read after the
    // refusal, so they include a strategy saved a moment before.
    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(SignalRepositoryQueries.Delete(name, cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == ForeignKeyViolation)
        {
            string[] strategies = [.. await connection.QueryAsync<string>(SignalRepositoryQueries.Strategies(name, cancellationToken))];
            throw new SignalLibraryException(
                SignalProblem.InUse,
                $"the signal {name} is used by the strategies {string.Join(", ", strategies)}",
                strategies: strategies);
        }
    }

    private static Task AddVersion(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, int version, SignalDocument signal, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(SignalRepositoryQueries.InsertVersion(id, version, signal, transaction, cancellationToken));
    }

    private static SignalLibraryException Taken(string name)
    {
        return new SignalLibraryException(SignalProblem.NameTaken, $"the name {name} is taken");
    }

    private static SignalLibraryException NotFound(string name)
    {
        return new SignalLibraryException(SignalProblem.NotFound, $"there is no signal {name}");
    }
}
