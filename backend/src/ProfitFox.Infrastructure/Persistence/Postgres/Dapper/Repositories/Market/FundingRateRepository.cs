using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class FundingRateRepository(NpgsqlDataSource dataSource) : IFundingRateRepository
{
    private const string Table = "funding_rates";
    private const string Column = "rate";

    public async Task UpsertAsync(Instrument instrument, IReadOnlyCollection<FundingRatePoint> points, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = MarketPointRepositoryQueries.Upsert(
            dataSource,
            Table,
            Column,
            instrument.Category.ToCode(),
            instrument.Symbol,
            [.. points.Select(point => (point.Time, point.Rate))]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FundingRatePoint>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<MarketPointDb> points = await connection.QueryAsync<MarketPointDb>(
            MarketPointRepositoryQueries.Get(Table, Column, instrument.Category.ToCode(), instrument.Symbol, from, to, limit, cancellationToken));

        return [.. points.Select(point => new FundingRatePoint(point.Time, point.Value))];
    }
}