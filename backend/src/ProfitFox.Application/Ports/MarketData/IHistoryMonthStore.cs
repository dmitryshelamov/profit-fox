using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     Where the cache of history keeps whole months (change add-backtest-history-cache, design.md, decision 1): files on the
///     disk of the machine, or memory in tests. A month it cannot give, for whatever reason, is a month it does not have.
/// </summary>
public interface IHistoryMonthStore
{
    /// <summary>
    ///     The candles of the month with its coverage, as they were saved; null when the store has no such month.
    /// </summary>
    Task<HistoryRead<HistoryCandle>?> ReadCandles(HistoryMonth month, CancellationToken cancellation);

    /// <summary>
    ///     The funding payments of the month with its coverage, as they were saved; null when the store has no such month.
    /// </summary>
    Task<HistoryRead<HistoryPayment>?> ReadFunding(HistoryMonth month, CancellationToken cancellation);

    /// <summary>
    ///     Keeps the candles of the month, replacing what was kept for it.
    /// </summary>
    Task SaveCandles(HistoryMonth month, HistoryRead<HistoryCandle> read, CancellationToken cancellation);

    /// <summary>
    ///     Keeps the funding payments of the month, replacing what was kept for it.
    /// </summary>
    Task SaveFunding(HistoryMonth month, HistoryRead<HistoryPayment> read, CancellationToken cancellation);
}
