using Dapper;

using Npgsql;

using ProfitFox.Application.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;

internal sealed class IdeaAutoRepository(NpgsqlDataSource dataSource) : IIdeaAutoRepository
{
    public async Task<IReadOnlyList<IdeaAuto>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaAutoDb>(IdeaAutoRepositoryQueries.GetAll(cancellationToken))).Select(IdeaAutoDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<IdeaAuto>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaAutoDb>(IdeaAutoRepositoryQueries.GetEnabled(cancellationToken))).Select(IdeaAutoDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyDictionary<Guid, IdeaAuto>> GetEveryAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return (await connection.QueryAsync<IdeaAutoDb>(IdeaAutoRepositoryQueries.GetEvery(cancellationToken))).Select(IdeaAutoDbConverter.ToDomain).ToDictionary(auto => auto.Id);
    }

    public async Task<IdeaAuto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaAutoDb? row = await connection.QuerySingleOrDefaultAsync<IdeaAutoDb>(IdeaAutoRepositoryQueries.Get(id, cancellationToken));

        return row is null ? null : IdeaAutoDbConverter.ToDomain(row);
    }

    public async Task<IdeaAuto> CreateAsync(IdeaAuto auto, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        int position = await connection.ExecuteScalarAsync<int>(IdeaAutoRepositoryQueries.Create(IdeaAutoDbConverter.FromDomain(auto), now, cancellationToken));

        return auto with { Position = position };
    }

    public async Task<bool> SaveAsync(IdeaAuto auto, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(IdeaAutoRepositoryQueries.Save(IdeaAutoDbConverter.FromDomain(auto), cancellationToken)) > 0;
    }

    public async Task<bool> SetModeAsync(Guid id, IdeaAutoMode mode, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(IdeaAutoRepositoryQueries.SetMode(id, mode.ToCode(), cancellationToken)) > 0;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(IdeaAutoRepositoryQueries.Reorder([.. ids], cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(IdeaAutoRepositoryQueries.Delete(id, now, cancellationToken)) > 0;
    }
}