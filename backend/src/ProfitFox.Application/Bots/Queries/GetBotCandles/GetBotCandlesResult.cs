using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Bots.Queries.GetBotCandles;

/// <summary>Either the candles or validation errors keyed by parameter name; <see cref="Unavailable" /> says the exchange could not fill the period.</summary>
public sealed record GetBotCandlesResult(
    Interval Interval,
    IReadOnlyList<Candle>? Candles,
    bool Unavailable,
    IReadOnlyDictionary<string, string> Errors);