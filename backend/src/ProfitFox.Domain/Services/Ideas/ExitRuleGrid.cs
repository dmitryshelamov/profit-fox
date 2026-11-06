using System.Collections.Concurrent;

using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The lifetimes and entry windows an exit rule takes, in candles of its interval (decision 2): the steps of the grid no longer than 7 days,
///     and 7 days themselves. A time goes up to the grid.
/// </summary>
public static class ExitRuleGrid
{
    private static readonly int[] Steps = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536];
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    // Each grid is made once: a path of a signal reads it every minute.
    private static readonly ConcurrentDictionary<Interval, int[]> Grids = new ConcurrentDictionary<Interval, int[]>();

    public static IReadOnlyList<int> Of(Interval interval)
    {
        return Grids.GetOrAdd(interval, key =>
        {
            int week = (int)(Week / Length(key));

            return [.. Steps.Where(step => step < week), week];
        });
    }

    /// <summary>The candles of the grid that cover <paramref name="time" />: the first step at or above it.</summary>
    public static int Candles(Interval interval, TimeSpan time)
    {
        double candles = time / Length(interval);

        return Of(interval).First(step => step >= candles);
    }

    private static TimeSpan Length(Interval interval)
    {
        return interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch;
    }
}