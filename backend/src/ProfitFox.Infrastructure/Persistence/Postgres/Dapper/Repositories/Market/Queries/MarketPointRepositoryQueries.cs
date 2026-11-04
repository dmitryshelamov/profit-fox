using Dapper;

using Npgsql;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

/// <summary>The SQL of the point tables: <c>open_interest</c> with its <c>value</c>, <c>funding_rates</c> with its <c>rate</c>.</summary>
internal static class MarketPointRepositoryQueries
{
    /// <param name="dataSource"></param>

    /// <param name="table"></param>
    /// <param name="column"></param>
    /// <param name="category"></param>
    /// <param name="symbol"></param>
    /// <param name="points"></param>    /// <param name="replace">Whether a point replaces the stored one of the same time, or the stored one stays.</param>
    internal static NpgsqlCommand Upsert(
        NpgsqlDataSource dataSource,
        string table,
        string column,
        string category,
        string symbol,
        IReadOnlyCollection<(DateTimeOffset Time, decimal Value)> points,
        bool replace = true)
    {
        // One statement for all points. Arrays go as Npgsql parameters: Dapper would expand them into IN lists.
        NpgsqlCommand command = dataSource.CreateCommand(
            $"""
             INSERT INTO {table} (category, symbol, "time", {column})
             SELECT @category, @symbol, t."time", t.value FROM unnest(@times, @values) AS t("time", value)
             ON CONFLICT (category, symbol, "time") {(replace ? $"DO UPDATE SET {column} = EXCLUDED.{column}" : "DO NOTHING")}
             """);
        command.Parameters.AddRange(
            new[]
            {
                new NpgsqlParameter("category", category), new NpgsqlParameter("symbol", symbol),
                new NpgsqlParameter("times", points.Select(point => point.Time).ToArray()), new NpgsqlParameter("values", points.Select(point => point.Value).ToArray())
            });

        return command;
    }

    internal static CommandDefinition Get(string table, string column, string category, string symbol, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             SELECT "time", {column} AS value
             FROM {table}
             WHERE category = @Category AND symbol = @Symbol AND "time" >= @From AND "time" < @To
             ORDER BY "time"
             LIMIT @Limit
             """,
            new { Category = category, Symbol = symbol, From = from, To = to, Limit = limit },
            cancellationToken: cancellationToken);
    }
}