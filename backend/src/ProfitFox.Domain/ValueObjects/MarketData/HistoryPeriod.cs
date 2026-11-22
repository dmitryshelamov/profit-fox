namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     The bounds of the history the bench reads (change add-strategy-backtest, design.md, decisions 4 and 8): from
///     <see cref="DataStart" />, earlier than any history the service keeps, to the start of the current UTC day at most.
/// </summary>
public static class HistoryPeriod
{
    /// <summary>
    ///     Earlier than any history the service keeps: the start of every read of the candles of a signal.
    /// </summary>
    public static readonly DateTimeOffset DataStart = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     The start of the UTC day of <paramref name="now" />.
    /// </summary>
    public static DateTimeOffset Today(DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    ///     Whether <paramref name="moment" /> is the start of a UTC day.
    /// </summary>
    public static bool IsDayStart(DateTimeOffset moment)
    {
        return moment.Offset == TimeSpan.Zero && moment.TimeOfDay == TimeSpan.Zero;
    }
}
