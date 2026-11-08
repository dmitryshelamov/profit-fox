using Dapper;

using Npgsql;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

internal static class LiquidationRepositoryQueries
{
    /// <summary>One statement for the whole batch; an event already stored adds no row and is not counted.</summary>
    internal static NpgsqlCommand Add(
        NpgsqlDataSource dataSource,
        IReadOnlyCollection<(string Category, string Symbol, DateTimeOffset Time, string Side, decimal Price, decimal Quantity)> rows)
    {
        // Arrays go as Npgsql parameters: Dapper would expand them into IN lists.
        NpgsqlCommand command = dataSource.CreateCommand(
            """
            WITH inserted AS (
                INSERT INTO liquidations (category, symbol, "time", side, price, quantity)
                SELECT * FROM unnest(@categories, @symbols, @times, @sides, @prices, @quantities)
                ON CONFLICT (category, symbol, "time", side, price, quantity) DO NOTHING
                RETURNING side
            )
            SELECT side, count(*)::int FROM inserted GROUP BY side
            """);
        command.Parameters.AddRange(
            new[]
            {
                new NpgsqlParameter("categories", rows.Select(row => row.Category).ToArray()),
                new NpgsqlParameter("symbols", rows.Select(row => row.Symbol).ToArray()),
                new NpgsqlParameter("times", rows.Select(row => row.Time).ToArray()),
                new NpgsqlParameter("sides", rows.Select(row => row.Side).ToArray()),
                new NpgsqlParameter("prices", rows.Select(row => row.Price).ToArray()),
                new NpgsqlParameter("quantities", rows.Select(row => row.Quantity).ToArray())
            });

        return command;
    }

    internal static CommandDefinition Get(string category, string symbol, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            SELECT "time", side, price, quantity
            FROM liquidations
            WHERE category = @Category AND symbol = @Symbol AND "time" >= @From AND "time" < @To
            ORDER BY "time", side, price, quantity
            LIMIT @Limit
            """,
            new { Category = category, Symbol = symbol, From = from, To = to, Limit = limit },
            cancellationToken: cancellationToken);
    }

    internal static NpgsqlCommand LastTime(NpgsqlDataSource dataSource)
    {
        return dataSource.CreateCommand("""SELECT max("time") FROM liquidations""");
    }
}