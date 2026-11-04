using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Domain.Services.Market;

/// <summary>
///     The metrics of a pair on the minute that ends at <c>end</c>, from its closed minutes, its points of open interest and its ticker.
///     A pure function of its input: the scanner computes the snapshot with it, and the setups of the next changes replay history with
///     it.
/// </summary>
public static class MarketMetrics
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    /// <param name="end">The end of the last closed minute.</param>
    /// <param name="coveredFrom">The start of the minutes storage holds in full up to <paramref name="coveredUntil" />.</param>
    /// <param name="coveredUntil">The end of the minutes storage holds in full.</param>
    /// <param name="minutes">The stored minutes, oldest first; a minute of the covered range that is missing had no trades.</param>
    /// <param name="openInterest">The points of open interest, in coins, oldest first.</param>
    /// <param name="ticker">The last ticker of the pair.</param>
    public static PairMetrics Compute(
        DateTimeOffset end,
        DateTimeOffset? coveredFrom,
        DateTimeOffset? coveredUntil,
        IReadOnlyList<MarketMinute> minutes,
        IReadOnlyList<OpenInterestPoint> openInterest,
        MarketTicker? ticker)
    {
        var window = new Window(end, coveredFrom, coveredUntil, minutes);

        // The newest point is the one at the start of the current 5 minutes.
        DateTimeOffset step = end.AddTicks(-(end.UtcTicks % FiveMinutes.Ticks));
        decimal? openInterestNow = Point(openInterest, step);

        return new PairMetrics(
            ticker?.LastPrice,
            window.Change(5),
            window.Change(15),
            window.Change(60),
            window.Change(240),
            window.Change(1440),
            window.Turnover(60),
            window.Turnover(1440),
            ticker?.OpenInterest * ticker?.LastPrice,
            Percent(Point(openInterest, step.AddHours(-1)), openInterestNow),
            Percent(Point(openInterest, step.AddDays(-1)), openInterestNow),
            ticker?.FundingRate,
            ticker is { Bid: { } bid, Ask: { } ask } ? Ratio(ask - bid, (ask + bid) / 2) : null);
    }

    private static decimal? Point(IReadOnlyList<OpenInterestPoint> points, DateTimeOffset time)
    {
        return points.FirstOrDefault(point => point.Time == time)?.Value;
    }

    private static decimal? Percent(decimal? from, decimal? to)
    {
        return Ratio(to - from, from);
    }

    /// <summary>The share in percent; a base of zero, such as the open interest of a pair just listed or an empty book, gives none.</summary>
    private static decimal? Ratio(decimal? part, decimal? whole)
    {
        return whole == 0 ? null : part / whole * 100;
    }

    /// <summary>The minutes before <c>end</c>; a window counts only when the covered range holds it whole.</summary>
    private sealed class Window(DateTimeOffset end, DateTimeOffset? coveredFrom, DateTimeOffset? coveredUntil, IReadOnlyList<MarketMinute> minutes)
    {
        /// <summary>From the open of the first minute to the close of the last; a minute without trades opens and closes at the close before it.</summary>
        public decimal? Change(int count)
        {
            DateTimeOffset from = end - (Minute * count);

            if (!Covers(from))
            {
                return null;
            }

            decimal? open = minutes.FirstOrDefault(minute => minute.OpenTime == from)?.Open ?? minutes.LastOrDefault(minute => minute.OpenTime < from)?.Close;

            return Percent(open, minutes.LastOrDefault(minute => minute.OpenTime < end)?.Close);
        }

        public decimal? Turnover(int count)
        {
            DateTimeOffset from = end - (Minute * count);

            return Covers(from) ? minutes.Where(minute => minute.OpenTime >= from && minute.OpenTime < end).Sum(minute => minute.Turnover) : null;
        }

        private bool Covers(DateTimeOffset from)
        {
            return coveredFrom <= from && coveredUntil >= end;
        }
    }
}