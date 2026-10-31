using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;

internal sealed class CandleImportJobRepository(NpgsqlDataSource dataSource) : ICandleImportJobRepository
{
    public async Task AddAsync(CandleImportJob job, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(CandleImportJobRepositoryQueries.Insert(CandleImportJobDbConverter.FromDomain(job), cancellationToken));
    }

    public async Task<CandleImportJob?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CandleImportJobDb? job = await connection.QuerySingleOrDefaultAsync<CandleImportJobDb>(CandleImportJobRepositoryQueries.Get(id, cancellationToken));
        return job is null ? null : CandleImportJobDbConverter.ToDomain(job);
    }

    public async Task UpdateAsync(CandleImportJob job, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(CandleImportJobRepositoryQueries.Update(CandleImportJobDbConverter.FromDomain(job), cancellationToken));
    }

    public async Task FailUnfinishedAsync(string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(CandleImportJobRepositoryQueries.FailUnfinished(reason, now, cancellationToken));
    }
}
