using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Application.MarketData.Queries.GetCandles;

/// <summary>Either the candles or validation errors keyed by parameter name.</summary>
public sealed record GetCandlesResult(IReadOnlyList<Candle>? Candles, IReadOnlyDictionary<string, string> Errors);