namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

/// <summary>
///     The pieces of the statements of the list of the market; the columns and conditions come from
///     <see cref="Lists.ListColumns" />.
/// </summary>
internal static class MarketListQueries
{
    public const string Columns =
        """
        m.symbol AS "Symbol", m.price AS "Price", m.change_1h AS "Change1h", m.change_4h AS "Change4h", m.change_24h AS "Change24h",
        m.turnover_24h AS "Turnover24h", m.open_interest AS "OpenInterest", m.open_interest_usdt AS "OpenInterestUsdt", m.funding_rate AS "FundingRate",
        m.next_funding AS "NextFunding", m.updated_at AS "UpdatedAt", m.kind AS "Kind"
        """;

    // A part of the symbol, whatever the case; the text is a value, not a pattern.
    public const string Search = "(@Search IS NULL OR strpos(upper(m.symbol), upper(@Search)) > 0)";

    // The open ideas of a pair that match the choices of an idea and the conditions of an idea.
    public static string OpenIdea(string conditions)
    {
        return $"""
                EXISTS (
                    SELECT 1 FROM ideas i LEFT JOIN strategies s ON s.id = i.strategy_id
                    WHERE i.symbol = m.symbol AND i.state = 'open'
                      AND (@Strategy IS NULL OR COALESCE(s.name, i.strategy_name) = @Strategy)
                      AND (@Timeframe IS NULL OR i.timeframe = @Timeframe)
                      AND {conditions})
                """;
    }

    public const string Badges =
        """
        SELECT i.symbol AS "Symbol", COALESCE(s.name, i.strategy_name) AS "Strategy", i.timeframe AS "Timeframe", i.side AS "Side"
        FROM ideas i LEFT JOIN strategies s ON s.id = i.strategy_id
        WHERE i.state = 'open' AND i.symbol = ANY(@Symbols)
        ORDER BY i.moment DESC, i.id
        """;
}
