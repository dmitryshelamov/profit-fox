namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>
///     The metrics of a pair on the last closed minute; a metric without every minute or point of its window is <c>null</c>. Changes,
///     spread and the changes of open interest are in percent; turnovers and open interest in USDT; the funding rate as the exchange
///     reports it.
/// </summary>
public sealed record PairMetrics(
    decimal? Price,
    decimal? Change5m,
    decimal? Change15m,
    decimal? Change1h,
    decimal? Change4h,
    decimal? Change24h,
    decimal? Turnover1h,
    decimal? Turnover24h,
    decimal? OpenInterest,
    decimal? OpenInterestChange1h,
    decimal? OpenInterestChange24h,
    decimal? FundingRate,
    decimal? Spread);