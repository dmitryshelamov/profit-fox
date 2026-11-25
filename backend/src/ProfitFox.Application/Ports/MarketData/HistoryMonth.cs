namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     A calendar month of UTC of one kind of history of one instrument: the candles of <see cref="Interval" />, or the
///     funding payments when it is null (change add-backtest-history-cache, design.md, decision 2).
/// </summary>
/// <param name="InstrumentId">The instrument as the market-data service names it.</param>
/// <param name="Interval">The interval of the candles; null for funding.</param>
/// <param name="Start">The first moment of the month, UTC.</param>
public readonly record struct HistoryMonth(string InstrumentId, TimeSpan? Interval, DateTimeOffset Start)
{
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
