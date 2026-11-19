using ProfitFox.Bench.Infrastructure.MarketData.Protos;

namespace ProfitFox.Bench.Infrastructure.MarketData;

/// <summary>
///     The candle intervals of the market-data service.
/// </summary>
public static class Intervals
{
    public static CandleInterval ToProto(TimeSpan interval)
    {
        return interval.TotalMinutes switch
        {
            1 => CandleInterval.OneMinute,
            5 => CandleInterval.FiveMinutes,
            15 => CandleInterval.FifteenMinutes,
            60 => CandleInterval.OneHour,
            240 => CandleInterval.FourHours,
            1440 => CandleInterval.OneDay,
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "The service has candles of 1m, 5m, 15m, 1h, 4h and 1d.")
        };
    }
}
