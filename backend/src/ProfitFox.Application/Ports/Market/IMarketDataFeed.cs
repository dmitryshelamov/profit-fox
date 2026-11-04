using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.Market;

/// <summary>
///     The public data of the exchange the scanner reads over REST. Every request keeps to the scanner's own rate limit and takes one
///     attempt; a transient failure other than the rate limit is retried a few times.
/// </summary>
/// <remarks>
///     Every method throws <see cref="ExchangeRateLimitedException" /> when the exchange says the scanner sends too many requests, and
///     while the pause that follows lasts, without sending a request; <see cref="ExchangeUnavailableException" /> when the exchange cannot
///     answer for now; <see cref="ExchangeException" /> when it rejects the request.
/// </remarks>
public interface IMarketDataFeed
{
    /// <summary>Every instrument the exchange lists in the category.</summary>
    Task<IReadOnlyList<ListedInstrument>> ListInstrumentsAsync(MarketCategory category, CancellationToken cancellationToken);

    /// <summary>At most 1000 newest minute candles of the instrument with open time in <c>[from, to)</c>, oldest first, in one request.</summary>
    Task<IReadOnlyList<Candle>> GetMinuteCandlesAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The tickers of every instrument of the category, in one request.</summary>
    Task<IReadOnlyList<MarketTicker>> GetTickersAsync(MarketCategory category, CancellationToken cancellationToken);

    /// <summary>The 5 minute points of open interest of a linear instrument in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The funding settlements of a linear instrument in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<FundingRatePoint>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}