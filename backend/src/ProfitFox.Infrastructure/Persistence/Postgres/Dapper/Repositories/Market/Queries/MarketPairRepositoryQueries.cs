namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

/// <summary>
///     The statements on <c>market_pairs</c>, the snapshot of the market a row per pair.
/// </summary>
internal static class MarketPairRepositoryQueries
{
    // The whole snapshot in one statement: the pairs given are written, the others go.
    public const string Replace =
        """
        WITH given AS (
            SELECT * FROM unnest(@Symbol, @Price, @Change1h, @Change4h, @Change24h, @Turnover24h, @OpenInterest, @OpenInterestUsdt, @FundingRate, @NextFunding, @UpdatedAt)
                AS g(symbol, price, change_1h, change_4h, change_24h, turnover_24h, open_interest, open_interest_usdt, funding_rate, next_funding, updated_at)
        ), gone AS (
            DELETE FROM market_pairs WHERE symbol NOT IN (SELECT symbol FROM given)
        )
        INSERT INTO market_pairs (symbol, price, change_1h, change_4h, change_24h, turnover_24h, open_interest, open_interest_usdt, funding_rate, next_funding, updated_at)
        SELECT * FROM given
        ON CONFLICT (symbol) DO UPDATE SET
            price = EXCLUDED.price, change_1h = EXCLUDED.change_1h, change_4h = EXCLUDED.change_4h, change_24h = EXCLUDED.change_24h,
            turnover_24h = EXCLUDED.turnover_24h, open_interest = EXCLUDED.open_interest, open_interest_usdt = EXCLUDED.open_interest_usdt,
            funding_rate = EXCLUDED.funding_rate, next_funding = EXCLUDED.next_funding, updated_at = EXCLUDED.updated_at
        """;
}
