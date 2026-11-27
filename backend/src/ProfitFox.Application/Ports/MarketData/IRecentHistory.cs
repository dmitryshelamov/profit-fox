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
    ///     At most <paramref name="limit" /> closed candles of <paramref name="interval" /> closed by
    ///     <paramref name="before" />, the newest ones, oldest first; more than one answer of the service holds (1000) are read
    ///     in several.
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

    /// <summary>
    ///     The last point of open interest of each of the <paramref name="count" /> intervals of <paramref name="interval" />
    ///     before <paramref name="before" />, with its own time, oldest first; an interval without a point has none (change
    ///     add-idea-score, design.md, decision 6). More than 1000 intervals are read in several windows, as the candles.
    /// </summary>
    /// <exception cref="Exceptions.HistoryUnavailableException">The service did not answer.</exception>
    Task<RecentRead<OpenInterestPoint>> OpenInterestPoints(string instrumentId, TimeSpan interval, DateTimeOffset before, int count, CancellationToken cancellation);
}
