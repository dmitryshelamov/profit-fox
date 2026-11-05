using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class ScannerPairRepository(NpgsqlDataSource dataSource) : IScannerPairRepository
{
    public async Task<IReadOnlyList<ScannerPair>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<ScannerPairDb> pairs = await connection.QueryAsync<ScannerPairDb>(ScannerPairRepositoryQueries.GetAll(cancellationToken));

        return pairs.Select(ScannerPairDbConverter.ToDomain).ToList();
    }

    public async Task<ScannerPair?> GetAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        ScannerPairDb? pair = await connection.QuerySingleOrDefaultAsync<ScannerPairDb>(ScannerPairRepositoryQueries.Get(instrument, cancellationToken));

        return pair is null ? null : ScannerPairDbConverter.ToDomain(pair);
    }

    public async Task SaveAsync(IReadOnlyCollection<ScannerPair> pairs, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(ScannerPairRepositoryQueries.Save(pairs.Select(ScannerPairDbConverter.FromDomain), transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}