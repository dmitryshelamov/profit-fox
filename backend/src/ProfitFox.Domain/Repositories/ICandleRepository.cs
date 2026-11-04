using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

public interface ICandleRepository
{
    /// <summary>Inserts the candles or replaces the stored ones with the same open time.</summary>
    Task UpsertAsync(Instrument instrument, Interval interval, IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken);

    /// <summary>Inserts the candles of several instruments in one batch or replaces the stored ones with the same open time.</summary>
    Task UpsertAsync(Interval interval, IReadOnlyCollection<(Instrument Instrument, Candle Candle)> candles, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="limit" /> stored candles with open time in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<Candle>> GetAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Open times of the stored candles in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetOpenTimesAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The candles of <paramref name="interval" /> that lie whole in <c>[from, to)</c>, joined from the stored minutes in one query, oldest
    ///     first; a minute without a candle had no trades, as <c>IdeaCandles.Join</c> says. The volume is the turnover.
    /// </summary>
    Task<IReadOnlyList<IdeaCandle>> GetAggregatedAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}