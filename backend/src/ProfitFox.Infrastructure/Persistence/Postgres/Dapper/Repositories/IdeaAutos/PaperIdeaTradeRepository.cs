using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;

internal sealed class PaperIdeaTradeRepository(NpgsqlDataSource dataSource) : IPaperIdeaTradeRepository
{
    public async Task<IReadOnlyList<PaperIdeaTrade>> GetOpenAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeDb>(PaperTradeQueries.GetOpen(cancellationToken))).Select(PaperTradeDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<PaperIdeaTrade>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeDb>(PaperTradeQueries.GetAll(cancellationToken))).Select(PaperTradeDbConverter.ToDomain)];
    }
}