using System.Data;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

/// <summary>Every write bumps the library revision in the same transaction and writes nothing when the revision has moved on.</summary>
internal sealed class StrategyLibraryRepository(NpgsqlDataSource dataSource) : IStrategyLibraryRepository
{
    /// <summary>One snapshot, so the revision matches the graphs it was read with.</summary>
    public async Task<StrategyLibrary> LoadAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        long revision = await connection.ExecuteScalarAsync<long>(StrategyLibraryRepositoryQueries.Revision(transaction, cancellationToken));
        IEnumerable<StrategyDb> strategies = await connection.QueryAsync<StrategyDb>(StrategyLibraryRepositoryQueries.Strategies(transaction, cancellationToken));
        IEnumerable<CompositeNodeDb> composites = await connection.QueryAsync<CompositeNodeDb>(StrategyLibraryRepositoryQueries.Composites(transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new StrategyLibrary(revision, strategies.Select(StrategyDbConverter.ToDomain), composites.Select(CompositeNodeDbConverter.ToDomain));
    }

    public Task<bool> SaveStrategyAsync(Strategy strategy, long expectedRevision, CancellationToken cancellationToken)
    {
        return WriteAsync(
            expectedRevision,
            (connection, transaction) => connection.ExecuteAsync(
                StrategyLibraryRepositoryQueries.UpsertStrategy(StrategyDbConverter.FromDomain(strategy), transaction, cancellationToken)),
            cancellationToken);
    }

    public Task<bool> DeleteStrategyAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
    {
        return WriteAsync(
            expectedRevision,
            (connection, transaction) => connection.ExecuteAsync(StrategyLibraryRepositoryQueries.DeleteStrategy(id, transaction, cancellationToken)),
            cancellationToken);
    }

    public Task<bool> SaveCompositeAsync(CompositeNode composite, long expectedRevision, CancellationToken cancellationToken)
    {
        return WriteAsync(
            expectedRevision,
            (connection, transaction) => connection.ExecuteAsync(
                StrategyLibraryRepositoryQueries.UpsertComposite(CompositeNodeDbConverter.FromDomain(composite), transaction, cancellationToken)),
            cancellationToken);
    }

    public Task<bool> DeleteCompositeAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
    {
        return WriteAsync(
            expectedRevision,
            (connection, transaction) => connection.ExecuteAsync(StrategyLibraryRepositoryQueries.DeleteComposite(id, transaction, cancellationToken)),
            cancellationToken);
    }

    public async Task<Strategy?> GetStrategyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        StrategyDb? strategy = await connection.QuerySingleOrDefaultAsync<StrategyDb>(StrategyLibraryRepositoryQueries.Strategy(id, cancellationToken));

        return strategy is null ? null : StrategyDbConverter.ToDomain(strategy);
    }

    public async Task<IReadOnlyList<Strategy>> GetStrategiesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<StrategyDb>(StrategyLibraryRepositoryQueries.Strategies(null, cancellationToken))).Select(StrategyDbConverter.ToDomain)];
    }

    public async Task<CompositeNode?> GetCompositeAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CompositeNodeDb? node = await connection.QuerySingleOrDefaultAsync<CompositeNodeDb>(StrategyLibraryRepositoryQueries.Composite(id, cancellationToken));

        return node is null ? null : CompositeNodeDbConverter.ToDomain(node);
    }

    public async Task<IReadOnlyList<CompositeNode>> GetCompositesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<CompositeNodeDb>(StrategyLibraryRepositoryQueries.Composites(null, cancellationToken))).Select(CompositeNodeDbConverter.ToDomain)];
    }

    private async Task<bool> WriteAsync(long expectedRevision, Func<NpgsqlConnection, NpgsqlTransaction, Task<int>> write, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.Bump(expectedRevision, transaction, cancellationToken)) == 0)
        {
            return false;
        }

        await write(connection, transaction);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}