using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     Recent history of a pair, the current day included (change add-ideas-and-market, design.md, decisions 4 and 9): the
///     closed candles of a timeframe, the funding payments and the points of open interest, from the REST of the market-data
///     service. It warms the graphs of the scanner up and draws the chart of the page of a pair; unlike
///     <see cref="IHistorySource" />, it does not wait for computed days.
/// </summary>
public interface IRecentHistory
{
    /// <summary>
    ///     At most <paramref name="limit" /> (up to 1000) closed candles of <paramref name="interval" /> closed by
    ///     <paramref name="before" />, the newest ones, oldest first.
    /// </summary>
    /// <exception cref="Exceptions.HistoryUnavailableException">The service did not answer.</exception>
    Task<RecentRead<HistoryCandle>> Candles(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation);

    /// <summary>
    ///     The funding payments at times in [<paramref name="from" />, <paramref name="to" />), oldest first.
    /// </summary>
    /// <exception cref="Exceptions.HistoryUnavailableException">The service did not answer.</exception>
    Task<RecentRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);

    /// <summary>
    ///     At most <paramref name="limit" /> points of open interest, one per <paramref name="interval" />, before
    ///     <paramref name="before" />, oldest first. With <paramref name="steps" />, each attempt of the read runs under the
    ///     limit of the page of a pair (task 23.2).
    /// </summary>
    /// <exception cref="Exceptions.HistoryUnavailableException">The service did not answer.</exception>
    /// <exception cref="Exceptions.ReadTimedOutException">An attempt did not end within the limit of <paramref name="steps" />.</exception>
    Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation, IReadSteps? steps = null);
}
