using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

/// <summary>
///     The saved strategies in the tables <c>strategies</c>, <c>strategy_versions</c> and <c>strategy_signals</c> (spec
///     strategy-constructor «Версии стратегии»), as the signals are kept: a save that changes only the layout or the
///     description adds no version, which is told by comparing the graphs as jsonb. Each version writes, in its transaction, a
///     row for each of its signal nodes; a signal deleted a moment before fails the save by the foreign key. Every write of
///     a strategy writes its kind, which a save never changes.
/// </summary>
internal sealed class StrategyRepository(NpgsqlDataSource database) : IStrategyRepository
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    public async Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<string>(StrategyRepositoryQueries.Names(cancellationToken))];
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(StrategyRepositoryQueries.Exists(name, cancellationToken));
    }

    public async Task<StrategyRead?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        StrategyDb? row = await connection.QuerySingleOrDefaultAsync<StrategyDb>(StrategyRepositoryQueries.Read(name, cancellationToken));
        return row is null ? null : StrategyDbConverter.ToDomain(row);
    }

    public async Task<StrategyDocument> CreateAsync(StrategyDocument strategy, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid id = strategy.Id;
        try
        {
            await connection.ExecuteAsync(StrategyRepositoryQueries.Insert(id, strategy, transaction, cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw Taken(strategy.Name);
        }

        await AddVersion(connection, transaction, id, 1, strategy, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StrategyDocument.Restore(id, strategy.Name, strategy.Description, strategy.Graph, strategy.Layout, version: 1, kind: strategy.Kind);
    }

    // The row is locked, so two saves of one strategy do not take the same next number.
    public async Task<StrategyDocument> ReplaceAsync(StrategyDocument strategy, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        StrategyDb? current = await connection.QuerySingleOrDefaultAsync<StrategyDb>(StrategyRepositoryQueries.Current(strategy.Name, transaction, cancellationToken));
        if (current is null)
        {
            throw NotFound(strategy.Name);
        }

        if (StrategyKinds.TryParse(current.Kind, out StrategyKind kind) && kind != strategy.Kind)
        {
            throw KindChanged(strategy.Name, kind);
        }

        string graph = StrategyDbConverter.WriteGraph(strategy);
        bool same = await connection.ExecuteScalarAsync<bool>(StrategyRepositoryQueries.SameGraph(current.Id, current.Version, graph, transaction, cancellationToken));
        int next = same ? current.Version : current.Version + 1;
        if (!same)
        {
            await AddVersion(connection, transaction, current.Id, next, strategy, cancellationToken);
        }

        await connection.ExecuteAsync(StrategyRepositoryQueries.Update(current.Id, strategy, next, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return StrategyDocument.Restore(current.Id, strategy.Name, strategy.Description, strategy.Graph, strategy.Layout, next, kind: strategy.Kind);
    }

    public async Task RenameAsync(string name, string to, StrategyKind kind, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            int changed = await connection.ExecuteAsync(StrategyRepositoryQueries.Rename(name, to, kind, cancellationToken));
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

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(StrategyRepositoryQueries.Delete(name, cancellationToken));
    }

    public async Task<IReadOnlyList<string>> UsingSignalAsync(Guid signal, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<string>(StrategyRepositoryQueries.UsingSignal(signal, cancellationToken))];
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> UsingSignalsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<SignalStrategyDb> rows = await connection.QueryAsync<SignalStrategyDb>(StrategyRepositoryQueries.UsingSignals(cancellationToken));
        return rows
            .GroupBy(row => row.Signal, row => row.Name)
            .ToDictionary(group => group.Key, IReadOnlyList<string> (group) => [.. group]);
    }

    // The version and the rows of its signal nodes; a signal deleted since the checks fails the save as a whole.
    private static async Task AddVersion(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, int version, StrategyDocument strategy, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(StrategyRepositoryQueries.InsertVersion(id, version, strategy, transaction, cancellationToken));
        IReadOnlyList<StrategySignalDb> rows = StrategyDbConverter.ToSignalRows(id, version, strategy);
        if (rows.Count == 0)
        {
            return;
        }

        try
        {
            await connection.ExecuteAsync(StrategyRepositoryQueries.InsertSignals(rows, transaction, cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == ForeignKeyViolation)
        {
            throw new StrategyLibraryException(
                StrategyProblem.Invalid,
                $"a signal the strategy {strategy.Name} points at is not saved: it was deleted a moment ago");
        }
    }

    // The kind of a saved strategy is chosen when it is created (spec strategy-constructor «Вид при создании стратегии»).
    private static StrategyLibraryException KindChanged(string name, StrategyKind kind)
    {
        return new StrategyLibraryException(
            StrategyProblem.Invalid,
            $"the kind of the strategy {name} is {StrategyKinds.Name(kind)} and does not change",
            [StrategyKinds.Changed(name, kind)]);
    }

    private static StrategyLibraryException Taken(string name)
    {
        return new StrategyLibraryException(StrategyProblem.NameTaken, $"the name {name} is taken");
    }

    private static StrategyLibraryException NotFound(string name)
    {
        return new StrategyLibraryException(StrategyProblem.NotFound, $"there is no strategy {name}");
    }
}
