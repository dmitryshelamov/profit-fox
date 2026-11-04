using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>
///     The ticker of an instrument as the exchange reports it now: the last price, the best bid and ask, and on linear the open
///     interest in coins and the current funding rate.
/// </summary>
public sealed record MarketTicker(Instrument Instrument, decimal LastPrice, decimal? Bid, decimal? Ask, decimal? OpenInterest = null, decimal? FundingRate = null);