using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The ideas in <c>ideas</c> (change add-ideas-and-market, design.md, decisions 5 and 14). A second open idea of a strategy
///     on a pair and timeframe is refused by a unique index, so an idea is written once even by two scanners.
/// </summary>
internal sealed class IdeaRepository(NpgsqlDataSource database) : IIdeaRepository
{
    public async Task<bool> AddAsync(Idea idea, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.Insert, IdeaDbConverter.Parameters(idea), cancellationToken: cancellationToken)) == 1;
    }

    public async Task SaveAsync(Idea idea, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.Save, IdeaDbConverter.Parameters(idea), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Idea>> OpenAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<IdeaDb>(new CommandDefinition(IdeaRepositoryQueries.Open, cancellationToken: cancellationToken))).Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<Idea?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IdeaDb? row = await connection.QuerySingleOrDefaultAsync<IdeaDb>(new CommandDefinition(IdeaRepositoryQueries.Find, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : IdeaDbConverter.ToDomain(row);
    }
}
