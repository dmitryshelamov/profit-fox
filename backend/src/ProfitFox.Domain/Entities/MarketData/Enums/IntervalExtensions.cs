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