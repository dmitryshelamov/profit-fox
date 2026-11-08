using ProfitFox.Domain.Entities.MarketData.Enums;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>Invest API enums for domain values; they are the contract of T-Invest and may change on their own.</summary>
internal static class TInvestCodes
{
    private static readonly Dictionary<Interval, CandleInterval> Intervals = new Dictionary<Interval, CandleInterval>
    {
        [Interval.OneMinute] = CandleInterval._1Min,
        [Interval.ThreeMinutes] = CandleInterval._3Min,
        [Interval.FiveMinutes] = CandleInterval._5Min,
        [Interval.FifteenMinutes] = CandleInterval._15Min,
        [Interval.ThirtyMinutes] = CandleInterval._30Min,
        [Interval.OneHour] = CandleInterval.Hour,
        [Interval.TwoHours] = CandleInterval._2Hour,
        [Interval.FourHours] = CandleInterval._4Hour
    };

    // The longest period one GetCandles with the source Exchange may ask for on an interval.
    private static readonly Dictionary<Interval, TimeSpan> Windows = new Dictionary<Interval, TimeSpan>
    {
        [Interval.OneMinute] = TimeSpan.FromDays(1),
        [Interval.ThreeMinutes] = TimeSpan.FromDays(1),
        [Interval.FiveMinutes] = TimeSpan.FromDays(7),
        [Interval.FifteenMinutes] = TimeSpan.FromDays(21),
        [Interval.ThirtyMinutes] = TimeSpan.FromDays(21),
        [Interval.OneHour] = TimeSpan.FromDays(90),
        [Interval.TwoHours] = TimeSpan.FromDays(90),
        [Interval.FourHours] = TimeSpan.FromDays(90)
    };

    /// <exception cref="ArgumentOutOfRangeException">An interval T-Invest has no candles of in this change.</exception>
    internal static CandleInterval ToTInvest(this Interval interval)
    {
        return Intervals.TryGetValue(interval, out CandleInterval candle) ? candle : throw new ArgumentOutOfRangeException(nameof(interval), interval, "T-Invest has no candles of this interval.");
    }

    /// <summary>The longest period one request of candles may cover on the interval.</summary>
    internal static TimeSpan Window(this Interval interval)
    {
        return Windows.TryGetValue(interval, out TimeSpan window) ? window : throw new ArgumentOutOfRangeException(nameof(interval), interval, "T-Invest has no candles of this interval.");
    }
}