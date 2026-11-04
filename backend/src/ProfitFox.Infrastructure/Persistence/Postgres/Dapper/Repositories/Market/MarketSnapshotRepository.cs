using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class MarketSnapshotRepository(NpgsqlDataSource dataSource) : IMarketSnapshotRepository
{
    public async Task<bool> ReplaceAsync(Guid owner, IReadOnlyCollection<MarketSnapshotRow> rows, DateTimeOffset snapshotAt, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteScalarAsync<int?>(MarketSnapshotRepositoryQueries.LockLease(owner, transaction, cancellationToken)) is null)
        {
            return false;
        }

        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.Clear(transaction, cancellationToken));
        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.Insert(rows.Select(MarketSnapshotRowDbConverter.FromDomain), transaction, cancellationToken));
        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.SnapshotAt(snapshotAt, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<MarketSnapshotRow>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<MarketSnapshotRowDb> rows = await connection.QueryAsync<MarketSnapshotRowDb>(MarketSnapshotRepositoryQueries.GetAll(cancellationToken));

        return [.. rows.Select(MarketSnapshotRowDbConverter.ToDomain)];
    }

    public async Task<MarketSnapshotRow?> GetAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        MarketSnapshotRowDb? row = await connection.QuerySingleOrDefaultAsync<MarketSnapshotRowDb>(
            MarketSnapshotRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, cancellationToken));

        return row is null ? null : MarketSnapshotRowDbConverter.ToDomain(row);
    }
}