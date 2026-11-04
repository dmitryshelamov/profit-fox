using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class OpenInterestRepository(NpgsqlDataSource dataSource) : IOpenInterestRepository
{
    private const string Table = "open_interest";
    private const string Column = "value";

    public async Task UpsertAsync(Instrument instrument, IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = MarketPointRepositoryQueries.Upsert(
            dataSource,
            Table,
            Column,
            instrument.Category.ToCode(),
            instrument.Symbol,
            [.. points.Select(point => (point.Time, point.Value))]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(Instrument instrument, IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = MarketPointRepositoryQueries.Upsert(
            dataSource,
            Table,
            Column,
            instrument.Category.ToCode(),
            instrument.Symbol,
            [.. points.Select(point => (point.Time, point.Value))],
            replace: false);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OpenInterestPoint>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<MarketPointDb> points = await connection.QueryAsync<MarketPointDb>(
            MarketPointRepositoryQueries.Get(Table, Column, instrument.Category.ToCode(), instrument.Symbol, from, to, limit, cancellationToken));

        return [.. points.Select(point => new OpenInterestPoint(point.Time, point.Value))];
    }
}