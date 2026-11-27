using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     Where history comes from: the <c>History</c> streams of the market-data service, or a fake in tests.
/// </summary>
public interface IHistorySource
{
    Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);

    Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);

    /// <summary>
    ///     The points of open interest at times in [<paramref name="from" />, <paramref name="to" />) and the covered parts of
    ///     that window (change add-idea-score, spec market-history «Источник истории»).
    /// </summary>
    Task<HistoryRead<OpenInterestPoint>> ReadOpenInterest(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);
}
