using Bybit.Net.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>Bybit.Net enums for domain values; they are Bybit's contract and may change on their own.</summary>
internal static class BybitCodes
{
    private static readonly Dictionary<MarketCategory, Category> Categories = new()
    {
        [MarketCategory.Spot] = Category.Spot,
        [MarketCategory.Linear] = Category.Linear,
    };

    private static readonly Dictionary<Interval, KlineInterval> Intervals = new()
    {
        [Interval.OneMinute] = KlineInterval.OneMinute,
        [Interval.ThreeMinutes] = KlineInterval.ThreeMinutes,
        [Interval.FiveMinutes] = KlineInterval.FiveMinutes,
        [Interval.FifteenMinutes] = KlineInterval.FifteenMinutes,
        [Interval.ThirtyMinutes] = KlineInterval.ThirtyMinutes,
        [Interval.OneHour] = KlineInterval.OneHour,
        [Interval.TwoHours] = KlineInterval.TwoHours,
        [Interval.FourHours] = KlineInterval.FourHours,
        [Interval.SixHours] = KlineInterval.SixHours,
        [Interval.TwelveHours] = KlineInterval.TwelveHours,
        [Interval.OneDay] = KlineInterval.OneDay,
        [Interval.OneWeek] = KlineInterval.OneWeek,
        [Interval.OneMonth] = KlineInterval.OneMonth,
    };

    internal static Category ToBybit(this MarketCategory category) => Categories[category];

    internal static KlineInterval ToBybit(this Interval interval) => Intervals[interval];
}
