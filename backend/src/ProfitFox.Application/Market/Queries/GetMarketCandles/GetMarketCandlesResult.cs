using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Market.Queries.GetMarketCandles;

/// <summary>Either the candles or validation errors keyed by parameter name; <see cref="Unavailable" /> says the exchange could not fill the period.</summary>
public sealed record GetMarketCandlesResult(Interval Interval, IReadOnlyList<Candle>? Candles, bool Unavailable, IReadOnlyDictionary<string, string> Errors);