using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;

internal sealed class CandleRepository(NpgsqlDataSource dataSource) : ICandleRepository
{
    public async Task UpsertAsync(Instrument instrument, Interval interval, IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken)
    {
        var dbCandles = candles.Select(candle => CandleDbConverter.FromDomain(instrument, interval, candle)).ToList();
        await using NpgsqlCommand command = CandleRepositoryQueries.Upsert(dataSource, dbCandles);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<CandleDb> candles = await connection.QueryAsync<CandleDb>(
            CandleRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, interval.ToCode(), from, to, limit, cancellationToken));

        return candles.Select(CandleDbConverter.ToDomain).ToList();
    }
}