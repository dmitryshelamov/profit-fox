using Dapper;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

internal static class ScannerPairRepositoryQueries
{
    private const string GetAllSql = """
                                     SELECT category, symbol, listed, history_from AS "HistoryFrom", covered_until AS "CoveredUntil",
                                         oi_covered_until AS "OiCoveredUntil", funding_covered_until AS "FundingCoveredUntil", listed_at AS "ListedAt",
                                         tick_size AS "TickSize"
                                     FROM scanner_pairs
                                     """;

    private const string SaveSql = """
                                   INSERT INTO scanner_pairs (category, symbol, listed, history_from, covered_until, oi_covered_until, funding_covered_until, listed_at, tick_size)
                                   VALUES (@Category, @Symbol, @Listed, @HistoryFrom, @CoveredUntil, @OiCoveredUntil, @FundingCoveredUntil, @ListedAt, @TickSize)
                                   ON CONFLICT (category, symbol) DO UPDATE SET
                                       listed = EXCLUDED.listed, history_from = EXCLUDED.history_from, covered_until = EXCLUDED.covered_until,
                                       oi_covered_until = EXCLUDED.oi_covered_until, funding_covered_until = EXCLUDED.funding_covered_until,
                                       listed_at = EXCLUDED.listed_at, tick_size = EXCLUDED.tick_size
                                   """;

    internal static CommandDefinition GetAll(CancellationToken cancellationToken)
    {
        return new CommandDefinition(GetAllSql, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(Instrument instrument, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"{GetAllSql} WHERE category = @Category AND symbol = @Symbol",
            new { Category = instrument.Category.ToCode(), instrument.Symbol },
            cancellationToken: cancellationToken);
    }

    // Dapper runs the statement once per row, inside the one transaction of the call.
    internal static CommandDefinition Save(IEnumerable<ScannerPairDb> pairs, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SaveSql, pairs, transaction, cancellationToken: cancellationToken);
    }
}