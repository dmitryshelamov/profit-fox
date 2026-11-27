namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     A calendar month of UTC of one kind of history of one instrument (change add-backtest-history-cache, design.md,
///     decision 2): the candles of <see cref="Interval" />, the funding payments or the points of open interest, named by
///     <see cref="Kind" /> (change add-idea-score, design.md, decision 6).
/// </summary>
/// <param name="InstrumentId">The instrument as the market-data service names it.</param>
/// <param name="Kind">The kind of history.</param>
/// <param name="Interval">The interval of the candles; null for funding and open interest.</param>
/// <param name="Start">The first moment of the month, UTC.</param>
public readonly record struct HistoryMonth(string InstrumentId, HistoryKind Kind, TimeSpan? Interval, DateTimeOffset Start)
{
    /// <summary>
    ///     A month of the candles of <paramref name="interval" />, or of the funding payments when it is null.
    /// </summary>
    public HistoryMonth(string instrumentId, TimeSpan? interval, DateTimeOffset start)
        : this(instrumentId, interval is null ? HistoryKind.Funding : HistoryKind.Candles, interval, start)
    {
    }

    /// <summary>
    ///     A month of the points of open interest.
    /// </summary>
    public static HistoryMonth OpenInterest(string instrumentId, DateTimeOffset start)
    {
        return new HistoryMonth(instrumentId, HistoryKind.OpenInterest, null, start);
    }

    /// <summary>
    ///     The first moment of the next month: the month is [<see cref="Start" />, <see cref="End" />).
    /// </summary>
    public DateTimeOffset End => Start.AddMonths(1);

    /// <summary>
    ///     The start of the UTC month of <paramref name="moment" />.
    /// </summary>
    public static DateTimeOffset StartOf(DateTimeOffset moment)
    {
        DateTimeOffset utc = moment.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
