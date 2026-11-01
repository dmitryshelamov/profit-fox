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

    public async Task<CandleImportJob?> ClaimNextAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(CandleImportJobRepositoryQueries.LockClaims(transaction, cancellationToken));
        CandleImportJobDb? job = await connection.QuerySingleOrDefaultAsync<CandleImportJobDb>(
            CandleImportJobRepositoryQueries.ClaimNext(owner, now, leasedUntil, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return job is null ? null : CandleImportJobDbConverter.ToDomain(job);
    }

    public async Task<bool> RenewLeaseAsync(Guid id, Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(CandleImportJobRepositoryQueries.RenewLease(id, owner, leasedUntil, cancellationToken)) > 0;
    }

    public async Task<bool> UpdateLeasedAsync(CandleImportJob job, Guid owner, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(CandleImportJobRepositoryQueries.UpdateLeased(CandleImportJobDbConverter.FromDomain(job), owner, cancellationToken)) > 0;
    }
}