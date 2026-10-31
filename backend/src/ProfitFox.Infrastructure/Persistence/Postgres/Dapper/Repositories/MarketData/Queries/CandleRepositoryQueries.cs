using Dapper;
using Npgsql;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

internal static class CandleRepositoryQueries
{
    // One statement per page. Arrays go as Npgsql parameters: Dapper would expand them into IN lists.
    private const string UpsertSql = """
        INSERT INTO candles (category, symbol, "interval", open_time, open, high, low, close, volume, turnover)
        SELECT t.category, t.symbol, t."interval", t.open_time, t.open, t.high, t.low, t.close, t.volume, t.turnover
        FROM unnest(@categories, @symbols, @intervals, @open_times, @opens, @highs, @lows, @closes, @volumes, @turnovers)
            AS t(category, symbol, "interval", open_time, open, high, low, close, volume, turnover)
        ON CONFLICT (category, symbol, "interval", open_time) DO UPDATE SET
            open = EXCLUDED.open, high = EXCLUDED.high, low = EXCLUDED.low, close = EXCLUDED.close,
            volume = EXCLUDED.volume, turnover = EXCLUDED.turnover
        """;

    private const string GetSql = """
        SELECT category, symbol, "interval", open_time AS "OpenTime", open, high, low, close, volume, turnover
        FROM candles
        WHERE category = @Category AND symbol = @Symbol AND "interval" = @Interval AND open_time >= @From AND open_time < @To
        ORDER BY open_time
        LIMIT @Limit
        """;

    internal static NpgsqlCommand Upsert(NpgsqlDataSource dataSource, IReadOnlyCollection<CandleDb> candles)
    {
        var command = dataSource.CreateCommand(UpsertSql);
        command.Parameters.AddRange(new NpgsqlParameter[]
        {
            new("categories", candles.Select(c => c.Category).ToArray()),
            new("symbols", candles.Select(c => c.Symbol).ToArray()),
            new("intervals", candles.Select(c => c.Interval).ToArray()),
            new("open_times", candles.Select(c => c.OpenTime).ToArray()),
            new("opens", candles.Select(c => c.Open).ToArray()),
            new("highs", candles.Select(c => c.High).ToArray()),
            new("lows", candles.Select(c => c.Low).ToArray()),
            new("closes", candles.Select(c => c.Close).ToArray()),
            new("volumes", candles.Select(c => c.Volume).ToArray()),
            new("turnovers", candles.Select(c => c.Turnover).ToArray()),
        });
        return command;
    }

    internal static CommandDefinition Get(
        string category, string symbol, string interval, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken) =>
        new(
            GetSql,
            new { Category = category, Symbol = symbol, Interval = interval, From = from, To = to, Limit = limit },
            cancellationToken: cancellationToken);
}
