using Dapper;

using Npgsql;

using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

internal static class TradeFlowMinuteRepositoryQueries
{
    // The row of the lease is locked for the transaction, so a collector that lost the lease between the check and the write writes nothing.
    private const string LockLeaseSql = "SELECT 1 FROM trade_flow_state WHERE id = 1 AND lease_owner = @Owner FOR UPDATE";

    private const string UpsertSql = """
                                     INSERT INTO trade_flow_minutes (category, symbol, open_time, buy_volume, sell_volume, buy_turnover, sell_turnover, buy_trades, sell_trades)
                                     SELECT * FROM unnest(@categories, @symbols, @openTimes, @buyVolumes, @sellVolumes, @buyTurnovers, @sellTurnovers, @buyTrades, @sellTrades)
                                     ON CONFLICT (category, symbol, open_time) DO UPDATE SET
                                         buy_volume = EXCLUDED.buy_volume, sell_volume = EXCLUDED.sell_volume,
                                         buy_turnover = EXCLUDED.buy_turnover, sell_turnover = EXCLUDED.sell_turnover,
                                         buy_trades = EXCLUDED.buy_trades, sell_trades = EXCLUDED.sell_trades
                                     """;

    private const string DeleteDaySql = """
                                        DELETE FROM trade_flow_minutes
                                        WHERE category = @Category AND symbol = @Symbol AND open_time >= @From AND open_time < @To
                                        """;

    // time_bucket of TimescaleDB aligns buckets of timestamptz to 2000-01-03 UTC, which lies on the grid of every interval up to a day.
    private const string GetBarsSql = """
                                      SELECT time_bucket(make_interval(mins => @Minutes), open_time) AS "OpenTime",
                                          sum(buy_volume) AS "BuyVolume", sum(sell_volume) AS "SellVolume",
                                          sum(buy_turnover) AS "BuyTurnover", sum(sell_turnover) AS "SellTurnover",
                                          sum(buy_trades)::int AS "BuyTrades", sum(sell_trades)::int AS "SellTrades"
                                      FROM trade_flow_minutes
                                      WHERE category = @Category AND symbol = @Symbol AND open_time >= @From AND open_time < @To
                                      GROUP BY 1
                                      ORDER BY 1
                                      """;

    internal static CommandDefinition LockLease(Guid owner, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(LockLeaseSql, new { Owner = owner }, transaction, cancellationToken: cancellationToken);
    }

    /// <summary>One statement for the whole batch; arrays go as Npgsql parameters, since Dapper would expand them into IN lists.</summary>
    internal static NpgsqlCommand Upsert(NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyCollection<(string Category, string Symbol, TradeFlowMinute Minute)> rows)
    {
        var command = new NpgsqlCommand(UpsertSql, connection, transaction);
        command.Parameters.AddRange(
            new[]
            {
                new NpgsqlParameter("categories", rows.Select(row => row.Category).ToArray()),
                new NpgsqlParameter("symbols", rows.Select(row => row.Symbol).ToArray()),
                new NpgsqlParameter("openTimes", rows.Select(row => row.Minute.OpenTime).ToArray()),
                new NpgsqlParameter("buyVolumes", rows.Select(row => row.Minute.BuyVolume).ToArray()),
                new NpgsqlParameter("sellVolumes", rows.Select(row => row.Minute.SellVolume).ToArray()),
                new NpgsqlParameter("buyTurnovers", rows.Select(row => row.Minute.BuyTurnover).ToArray()),
                new NpgsqlParameter("sellTurnovers", rows.Select(row => row.Minute.SellTurnover).ToArray()),
                new NpgsqlParameter("buyTrades", rows.Select(row => row.Minute.BuyTrades).ToArray()),
                new NpgsqlParameter("sellTrades", rows.Select(row => row.Minute.SellTrades).ToArray())
            });

        return command;
    }

    internal static CommandDefinition DeleteDay(string category, string symbol, DateTimeOffset day, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            DeleteDaySql,
            new { Category = category, Symbol = symbol, From = day, To = day.AddDays(1) },
            transaction,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetBars(string category, string symbol, int minutes, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            GetBarsSql,
            new { Category = category, Symbol = symbol, Minutes = minutes, From = from, To = to },
            cancellationToken: cancellationToken);
    }

    internal static NpgsqlCommand First(NpgsqlDataSource dataSource, string category, string symbol)
    {
        NpgsqlCommand command = dataSource.CreateCommand("SELECT min(open_time) FROM trade_flow_minutes WHERE category = @category AND symbol = @symbol");
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("symbol", symbol);

        return command;
    }

    internal static NpgsqlCommand Last(NpgsqlDataSource dataSource)
    {
        return dataSource.CreateCommand("SELECT max(open_time) FROM trade_flow_minutes");
    }
}