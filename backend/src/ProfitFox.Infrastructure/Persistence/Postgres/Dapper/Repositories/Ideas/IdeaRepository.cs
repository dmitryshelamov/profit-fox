using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaRepository(NpgsqlDataSource dataSource) : IIdeaRepository
{
    public async Task<IReadOnlyList<Idea>> AddAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var added = new List<Idea>();

        foreach (Idea idea in ideas)
        {
            if (await connection.ExecuteScalarAsync<Guid?>(IdeaRepositoryQueries.Insert(IdeaDbConverter.FromDomain(idea), cancellationToken)) is not null)
            {
                added.Add(idea);
            }
        }

        return added;
    }

    public async Task UpdateAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(IdeaRepositoryQueries.Update(ideas.Select(idea => IdeaDbConverter.FromDomain(idea)), cancellationToken));
    }

    public async Task<IReadOnlyList<Idea>> GetOpenAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaDb>(IdeaRepositoryQueries.GetOpen(cancellationToken))).Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<Idea>> GetRecentAsync(DateTimeOffset closedSince, Instrument? instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<IdeaDb> rows = await connection.QueryAsync<IdeaDb>(
            IdeaRepositoryQueries.GetRecent(closedSince, instrument?.Category.ToCode(), instrument?.Symbol, cancellationToken));

        return [.. rows.Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<Idea?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaDb? row = await connection.QuerySingleOrDefaultAsync<IdeaDb>(IdeaRepositoryQueries.Get(id, cancellationToken));

        return row is null ? null : IdeaDbConverter.ToDomain(row);
    }
}