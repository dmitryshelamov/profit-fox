using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;

internal sealed class SeriesCoverageRepository(NpgsqlDataSource dataSource) : ISeriesCoverageRepository
{
    public async Task<SeriesCoverage?> GetAsync(Instrument instrument, Interval interval, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        SeriesCoverageDb? row = await connection.QuerySingleOrDefaultAsync<SeriesCoverageDb>(
            SeriesCoverageRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, interval.ToCode(), cancellationToken));

        return row is null ? null : new SeriesCoverage(row.CoveredFrom, row.CoveredUntil);
    }

    public async Task UpsertAsync(Instrument instrument, Interval interval, SeriesCoverage coverage, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            SeriesCoverageRepositoryQueries.Upsert(instrument.Category.ToCode(), instrument.Symbol, interval.ToCode(), coverage.From, coverage.Until, null, cancellationToken));
    }
}