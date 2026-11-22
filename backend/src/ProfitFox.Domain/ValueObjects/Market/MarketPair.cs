namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>
///     The snapshot of a pair (change add-ideas-and-market, spec market-overview «Снимок рынка»): the last price — the close
///     of its last minute —, the change of the price over 1, 4 and 24 hours as fractions, the turnover of 24 hours in USDT,
///     the open interest in coins and in USDT, the funding rate, the time of the next payment and when it was taken. A
///     change or a turnover a pair is too young for is null.
/// </summary>
public sealed record MarketPair(
    string Symbol,
    decimal Price,
    decimal? Change1h,
    decimal? Change4h,
    decimal? Change24h,
    decimal? Turnover24h,
    decimal? OpenInterest,
    decimal? OpenInterestUsdt,
    decimal? FundingRate,
    DateTimeOffset? NextFunding,
    DateTimeOffset UpdatedAt);
