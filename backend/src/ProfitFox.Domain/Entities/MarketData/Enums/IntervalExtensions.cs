namespace ProfitFox.Domain.Entities.MarketData.Enums;

public static class IntervalExtensions
{
    /// <summary>End of the candle that opens at <paramref name="openTime" />; a monthly candle ends at the next calendar month.</summary>
    public static DateTimeOffset CandleEnd(this Interval interval, DateTimeOffset openTime)
    {
        return interval switch
        {
            Interval.OneMinute => openTime.AddMinutes(1),
            Interval.ThreeMinutes => openTime.AddMinutes(3),
            Interval.FiveMinutes => openTime.AddMinutes(5),
            Interval.FifteenMinutes => openTime.AddMinutes(15),
            Interval.ThirtyMinutes => openTime.AddMinutes(30),
            Interval.OneHour => openTime.AddHours(1),
            Interval.TwoHours => openTime.AddHours(2),
            Interval.FourHours => openTime.AddHours(4),
            Interval.SixHours => openTime.AddHours(6),
            Interval.TwelveHours => openTime.AddHours(12),
            Interval.OneDay => openTime.AddDays(1),
            Interval.OneWeek => openTime.AddDays(7),
            _ => openTime.AddMonths(1)
        };
    }

    /// <summary>
    ///     Open time of the candle that contains <paramref name="at" />: a grid from the UTC epoch for minutes, hours and days,
    ///     Monday 00:00 for a week, the first of the month for a month. A moment on a boundary is its own candle's open time.
    /// </summary>
    public static DateTimeOffset CandleOpen(this Interval interval, DateTimeOffset at)
    {
        DateTimeOffset utc = at.ToUniversalTime();

        switch (interval)
        {
            case Interval.OneWeek:
                int sinceMonday = ((int)utc.DayOfWeek + 6) % 7;
                return new DateTimeOffset(utc.Date.AddDays(-sinceMonday), TimeSpan.Zero);
            case Interval.OneMonth:
                return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
            default:
                TimeSpan length = interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch;
                return DateTimeOffset.UnixEpoch + TimeSpan.FromTicks((utc - DateTimeOffset.UnixEpoch).Ticks / length.Ticks * length.Ticks);
        }
    }

    /// <summary>End of the last candle that opens in <c>[from, to)</c>, the first one at <paramref name="from" />; <paramref name="to" /> may lie inside it.</summary>
    public static DateTimeOffset LastCandleEnd(this Interval interval, DateTimeOffset from, DateTimeOffset to)
    {
        DateTimeOffset end = interval.CandleEnd(from);

        while (end < to)
        {
            end = interval.CandleEnd(end);
        }

        return end;
    }

    /// <summary>
    ///     How many candles open in <c>[from, to)</c>, the first one at <paramref name="from" />; counting stops at
    ///     <c><paramref name="limit" /> + 1</c>, so a huge period costs no more than the limit.
    /// </summary>
    public static long CandleCount(this Interval interval, DateTimeOffset from, DateTimeOffset to, long limit = long.MaxValue - 1)
    {
        long count = 0;

        for (DateTimeOffset openTime = from; openTime < to && count <= limit; openTime = interval.CandleEnd(openTime))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Ranges of <c>[from, to)</c> that no stored candle covers, oldest first; <paramref name="storedOpenTimes" /> are the open
    ///     times stored in the period, ascending. The next candle opens where the previous one ends, so no grid is needed.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> MissingRanges(
        this Interval interval,
        IReadOnlyList<DateTimeOffset> storedOpenTimes,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var missing = new List<(DateTimeOffset From, DateTimeOffset To)>();
        DateTimeOffset covered = from;

        foreach (DateTimeOffset openTime in storedOpenTimes)
        {
            if (openTime > covered)
            {
                missing.Add((covered, openTime));
            }

            covered = interval.CandleEnd(openTime);
        }

        if (covered < to)
        {
            missing.Add((covered, to));
        }

        return missing;
    }
}