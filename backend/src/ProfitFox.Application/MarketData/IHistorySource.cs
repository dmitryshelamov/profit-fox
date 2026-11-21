using ProfitFox.Domain.MarketData;

namespace ProfitFox.Application.MarketData;

/// <summary>
///     Where history comes from: the <c>History</c> streams of the market-data service, or a fake in tests.
/// </summary>
public interface IHistorySource
{
    Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);

    Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);
}
