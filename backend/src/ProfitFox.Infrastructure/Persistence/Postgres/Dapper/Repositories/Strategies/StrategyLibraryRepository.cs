using System.Data;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

/// <summary>Every save bumps the library revision in the same transaction and writes nothing when the revision has moved on.</summary>
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
        IEnumerable<StrategyVersionDb> versions = await connection.QueryAsync<StrategyVersionDb>(StrategyLibraryRepositoryQueries.LatestVersions(transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new StrategyLibrary(
            revision,
            strategies.Select(StrategyDbConverter.ToDomain),
            composites.Select(CompositeNodeDbConverter.ToDomain),
            versions.Select(StrategyVersionDbConverter.ToDomain));
    }

    /// <summary>Nodes and strategies go first, so the versions find their strategies; deletions cascade to versions.</summary>
    public async Task<bool> SaveAsync(StrategyLibraryChange change, long expectedRevision, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.Bump(expectedRevision, transaction, cancellationToken)) == 0)
        {
            return false;
        }

        foreach (CompositeNode composite in change.SavedComposites)
        {
            await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.UpsertComposite(CompositeNodeDbConverter.FromDomain(composite), transaction, cancellationToken));
        }

        foreach (Strategy strategy in change.SavedStrategies)
        {
            await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.UpsertStrategy(StrategyDbConverter.FromDomain(strategy), transaction, cancellationToken));
        }

        foreach (Guid id in change.RemovedStrategies)
        {
            await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.DeleteStrategy(id, transaction, cancellationToken));
        }

        foreach (Guid id in change.RemovedComposites)
        {
            await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.DeleteComposite(id, transaction, cancellationToken));
        }

        foreach (StrategyVersion version in change.NewVersions)
        {
            await connection.ExecuteAsync(StrategyLibraryRepositoryQueries.InsertVersion(StrategyVersionDbConverter.FromDomain(version), transaction, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
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

    public async Task<int?> GetLatestVersionNumberAsync(Guid strategyId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int?>(StrategyLibraryRepositoryQueries.LatestVersionNumber(strategyId, cancellationToken));
    }

    public async Task<StrategyVersion?> GetVersionAsync(Guid strategyId, int number, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        StrategyVersionDb? version = await connection.QuerySingleOrDefaultAsync<StrategyVersionDb>(StrategyLibraryRepositoryQueries.Version(strategyId, number, cancellationToken));

        return version is null ? null : StrategyVersionDbConverter.ToDomain(version);
    }
}