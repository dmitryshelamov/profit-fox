using System.Data;

using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

internal static class MarketSnapshotRepositoryQueries
{
    private const string Columns = """
                                   category, symbol, computed_at AS "ComputedAt", price, change_5m AS "Change5m", change_15m AS "Change15m",
                                   change_1h AS "Change1h", change_4h AS "Change4h", change_24h AS "Change24h", turnover_1h AS "Turnover1h",
                                   turnover_24h AS "Turnover24h", open_interest AS "OpenInterest", open_interest_change_1h AS "OpenInterestChange1h",
                                   open_interest_change_24h AS "OpenInterestChange24h", funding_rate AS "FundingRate", spread, incomplete
                                   """;

    // Locks the row of the state, so the lease cannot change hands until the snapshot is written.
    private const string LockLeaseSql = "SELECT 1 FROM scanner_state WHERE id = 1 AND lease_owner = @Owner FOR UPDATE";

    private const string ClearSql = "DELETE FROM market_snapshot";

    private const string InsertSql = """
                                     INSERT INTO market_snapshot (category, symbol, computed_at, price, change_5m, change_15m, change_1h, change_4h, change_24h,
                                         turnover_1h, turnover_24h, open_interest, open_interest_change_1h, open_interest_change_24h, funding_rate, spread, incomplete)
                                     VALUES (@Category, @Symbol, @ComputedAt, @Price, @Change5m, @Change15m, @Change1h, @Change4h, @Change24h,
                                         @Turnover1h, @Turnover24h, @OpenInterest, @OpenInterestChange1h, @OpenInterestChange24h, @FundingRate, @Spread, @Incomplete)
                                     """;

    private const string SnapshotAtSql = "UPDATE scanner_state SET snapshot_at = @SnapshotAt WHERE id = 1";

    internal static CommandDefinition LockLease(Guid owner, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(LockLeaseSql, new { Owner = owner }, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Clear(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ClearSql, transaction: transaction, cancellationToken: cancellationToken);
    }

    // Dapper runs the statement once per row, inside the one transaction.
    internal static CommandDefinition Insert(IEnumerable<MarketSnapshotRowDb> rows, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, rows, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition SnapshotAt(DateTimeOffset snapshotAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SnapshotAtSql, new { SnapshotAt = snapshotAt }, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetAll(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM market_snapshot", cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(string category, string symbol, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {Columns} FROM market_snapshot WHERE category = @Category AND symbol = @Symbol",
            new { Category = category, Symbol = symbol },
            cancellationToken: cancellationToken);
    }
}