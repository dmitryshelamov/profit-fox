using Dapper;

using Npgsql;

using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class LiquidationRepository(NpgsqlDataSource dataSource) : ILiquidationRepository
{
    public async Task<IReadOnlyDictionary<LiquidationSide, int>> AddAsync(
        IReadOnlyCollection<(Instrument Instrument, LiquidationEvent Event)> events,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = LiquidationRepositoryQueries.Add(
            dataSource,
            [.. events.Select(item => (item.Instrument.Category.ToCode(), item.Instrument.Symbol, item.Event.Time, item.Event.Side.ToCode(), item.Event.Price, item.Event.Quantity))]);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var inserted = new Dictionary<LiquidationSide, int>();

        while (await reader.ReadAsync(cancellationToken))
        {
            MarketCodes.TryParseLiquidationSide(reader.GetString(0), out LiquidationSide side);
            inserted[side] = reader.GetInt32(1);
        }

        return inserted;
    }

    public async Task<IReadOnlyList<LiquidationEvent>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<LiquidationDb> rows = await connection.QueryAsync<LiquidationDb>(
            LiquidationRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, from, to, limit, cancellationToken));

        return [.. rows.Select(LiquidationDbConverter.ToDomain)];
    }

    public async Task<DateTimeOffset?> LastTimeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = LiquidationRepositoryQueries.LastTime(dataSource);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        // Npgsql reads timestamptz as a UTC DateTimeOffset only when asked for one; Dapper would get a DateTime.
        return await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(0);
    }
}