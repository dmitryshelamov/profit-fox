namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>
///     A pair of the market (change add-ideas-and-market, spec market-overview «Снимок рынка»): the last price, the changes
///     over 1, 4 and 24 hours as fractions, the turnover of 24 hours in USDT, the open interest in coins and in USDT, the
///     funding rate as a fraction, the next payment and the time of the snapshot in milliseconds, and its open ideas. A value
///     the scanner has not counted is null. <see cref="Kind" /> — the kind of the pair, <c>crypto</c> or <c>stock</c> (change
///     add-instrument-kind-filter, decision 4).
/// </summary>
public sealed record MarketPairResponse(
    string Symbol,
    string Kind,
    double? Price,
    double? Change1h,
    double? Change4h,
    double? Change24h,
    double? Turnover24h,
    double? OpenInterest,
    double? OpenInterestUsdt,
    double? FundingRate,
    long? NextFunding,
    long? UpdatedAt,
    IReadOnlyList<IdeaBadgeResponse> Ideas);

/// <summary>
///     An open idea as a row of the market shows it.
/// </summary>
public sealed record IdeaBadgeResponse(string Strategy, string Timeframe, string Side);

/// <summary>
///     A page of the market and the time of its snapshot in milliseconds.
/// </summary>
public sealed record MarketPageResponse(IReadOnlyList<MarketPairResponse> Items, int Total, int Page, int PageSize, bool FilterGone, long? UpdatedAt);
