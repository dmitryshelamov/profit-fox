using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Services.Strategies;

/// <summary>
///     The cycles of a strategy with orders on one pair in a preview (change add-position-node, design.md, decision 9;
///     spec strategy-preview «Предел минутной истории»): its orders depend on the position, so the weeks with orders are not
///     known ahead; the cycles are computed over the last <see cref="PairOrders.MaxWeeks" /> weeks before the end, from a
///     closed position, and nothing before them is in the answer. The graph runs on every candle from the first, so its
///     memory is that of the whole history.
/// </summary>
public static class PairCycles
{
    /// <summary>
    ///     The minutes the cycles are computed on: the last <paramref name="maxWeeks" /> weeks before <paramref name="end" />,
    ///     not before <paramref name="start" />.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset start, DateTimeOffset end, int maxWeeks = PairOrders.MaxWeeks)
    {
        DateTimeOffset from = end - TimeSpan.FromDays(7 * maxWeeks);
        return (from > start ? from : start, end);
    }

    /// <summary>
    ///     The run of the strategy on the candles of its timeframe, the funding of the pair and the minute path of the window
    ///     that starts at <paramref name="from" />, finished at <paramref name="end" />. A cancelled
    ///     <paramref name="cancellationToken" /> stops the calculation: the request of the preview was given up.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static PositionResult Of(
        TradingStrategy strategy,
        TimeSpan timeframe,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding,
        MinutePath path,
        DateTimeOffset from,
        DateTimeOffset end,
        TradeCosts costs,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named = null)
    {
        var run = new PositionRun(strategy, path, costs, from, end, cancellationToken);
        foreach ((ClosedCandle candle, FundingPayment? lastFunding, IReadOnlyDictionary<string, ClosedCandle>? pairs) in PairCandles.WithPairs(timeframe, candles, funding, named))
        {
            if (candle.CloseTime > end)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            run.Step(candle, lastFunding, pairs);
        }

        return run.Finish(end);
    }
}
