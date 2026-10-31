using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.MarketData;

/// <summary>Trading aggregate of one instrument over one interval; values are exactly as the exchange reported them.</summary>
public sealed record Candle(
    DateTimeOffset OpenTime,
    Price Open,
    Price High,
    Price Low,
    Price Close,
    Volume Volume,
    Volume Turnover)
{
    public bool IsClosedAt(Interval interval, DateTimeOffset now)
    {
        return interval.CandleEnd(OpenTime) <= now;
    }
}