using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaVolatilityRepository(NpgsqlDataSource dataSource) : IIdeaVolatilityRepository
{
    public async Task<IReadOnlyList<IdeaVolatility>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>(
            new CommandDefinition("""SELECT category, symbol, "interval", percentile FROM idea_volatility""", cancellationToken: cancellationToken));

        return
        [
            .. rows.Select(row =>
            {
                MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);
                MarketDataCodes.TryParseInterval(row.Interval, out Interval interval);

                return new IdeaVolatility(new Instrument(category, row.Symbol), interval, row.Percentile, null);
            })
        ];
    }

    public async Task SaveAsync(IReadOnlyCollection<IdeaVolatility> percentiles, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO idea_volatility (category, symbol, "interval", percentile) VALUES (@Category, @Symbol, @Interval, @Percentile)
                ON CONFLICT (category, symbol, "interval") DO UPDATE SET percentile = EXCLUDED.percentile
                """,
                percentiles.Select(item => new Row
                {
                    Category = item.Instrument.Category.ToCode(),
                    Symbol = item.Instrument.Symbol,
                    Interval = item.Interval.ToCode(),
                    Percentile = item.Percentile
                }),
                cancellationToken: cancellationToken));
    }

    private sealed class Row
    {
        public required string Category { get; init; }

        public required string Symbol { get; init; }

        public required string Interval { get; init; }

        public decimal Percentile { get; init; }
    }
}