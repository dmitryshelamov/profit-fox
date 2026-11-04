using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The 90th percentile of ATR(14) / price of a pair on an interval over its last 90 days, which «Высокая волатильность» exceeds.</summary>
public sealed record IdeaVolatility(Instrument Instrument, Interval Interval, decimal Percentile);