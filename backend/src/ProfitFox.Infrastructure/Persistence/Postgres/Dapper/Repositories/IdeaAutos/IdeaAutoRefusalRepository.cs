using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;

internal sealed class IdeaAutoRefusalRepository(NpgsqlDataSource dataSource, PostgresTransactionAccessor accessor) : IIdeaAutoRefusalRepository
{
    public async Task AddAsync(IdeaAutoRefusal refusal, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaAutoRefusalRepositoryQueries.Insert(IdeaAutoDbConverter.FromDomain(refusal), current.Transaction, cancellationToken));
    }

    public async Task<(IReadOnlyList<IdeaAutoRefusal> Items, int Total)> GetPageAsync(DateTimeOffset since, Guid? autoId, int skip, int take, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        int total = await connection.ExecuteScalarAsync<int>(IdeaAutoRefusalRepositoryQueries.Count(since, autoId, cancellationToken));
        IEnumerable<IdeaAutoRefusalDb> rows = await connection.QueryAsync<IdeaAutoRefusalDb>(IdeaAutoRefusalRepositoryQueries.GetPage(since, autoId, skip, take, cancellationToken));

        return ([.. rows.Select(IdeaAutoDbConverter.ToDomain)], total);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<AutoCount> rows = await connection.QueryAsync<AutoCount>(IdeaAutoRefusalRepositoryQueries.CountByAuto(since, cancellationToken));

        return rows.ToDictionary(row => row.AutoId, row => row.Count);
    }

    public async Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(IdeaAutoRefusalRepositoryQueries.DeleteBefore(before, cancellationToken));
    }

    private sealed class AutoCount
    {
        public Guid AutoId { get; init; }

        public int Count { get; init; }
    }
}