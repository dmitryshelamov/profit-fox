using Dapper;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

internal static class SeriesCoverageRepositoryQueries
{
    private const string GetSql = """
                                  SELECT covered_from AS "CoveredFrom", covered_until AS "CoveredUntil"
                                  FROM candle_coverage
                                  WHERE category = @Category AND symbol = @Symbol AND "interval" = @Interval
                                  """;

    private const string UpsertSql = """
                                     INSERT INTO candle_coverage (category, symbol, "interval", covered_from, covered_until)
                                     VALUES (@Category, @Symbol, @Interval, @From, @Until)
                                     ON CONFLICT (category, symbol, "interval") DO UPDATE SET covered_from = EXCLUDED.covered_from, covered_until = EXCLUDED.covered_until
                                     """;

    internal static CommandDefinition Get(string category, string symbol, string interval, CancellationToken cancellationToken)
    {
        return new CommandDefinition(GetSql, new { Category = category, Symbol = symbol, Interval = interval }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Upsert(
        string category,
        string symbol,
        string interval,
        DateTimeOffset from,
        DateTimeOffset until,
        System.Data.IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            UpsertSql,
            new { Category = category, Symbol = symbol, Interval = interval, From = from, Until = until },
            transaction,
            cancellationToken: cancellationToken);
    }
}