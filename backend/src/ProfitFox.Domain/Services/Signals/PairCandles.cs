using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Signals;

/// <summary>
///     The candles of one pair as a graph gets them, a signal and a strategy alike (spec signal-preview): only the candles
///     with trades, in the order given, each with the last funding payment paid before its close. A payment at <c>T</c>
///     belongs to the candle that closes after <c>T</c> (change add-ideas-and-market, task 27.3, engine version 4): live the
///     payment comes a minute or three after its time, so the candle that closes at <c>T</c> does not know it yet. The points
///     of open interest go the same way (change add-idea-score, design.md, decision 5): the last point with a time strictly
///     before the close; a candle whose interval has no point takes the point of an interval before.
/// </summary>
public static class PairCandles
{
    public static IEnumerable<(ClosedCandle Candle, CandleFacts Facts)> Closed(
        TimeSpan timeframe,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding,
        IEnumerable<OpenInterestPoint>? openInterest = null)
    {
        FundingPayment[] payments = [.. funding.Select(payment => payment.ToFundingPayment()).OrderBy(payment => payment.Time)];
        OpenInterestPoint[] points = openInterest is null ? [] : [.. openInterest.OrderBy(point => point.Time)];
        int paid = 0;
        int seen = 0;
        foreach (HistoryCandle candle in candles.Where(candle => candle.HasTrades))
        {
            var closed = candle.ToClosedCandle(timeframe);
            while (paid < payments.Length && payments[paid].Time < closed.CloseTime)
            {
                paid++;
            }

            while (seen < points.Length && points[seen].Time < closed.CloseTime)
            {
                seen++;
            }

            yield return (closed, new CandleFacts(paid > 0 ? payments[paid - 1] : null, seen > 0 ? points[seen - 1] : null));
        }
    }

    /// <summary>
    ///     The candles of <see cref="Closed" />, each with the candles of the pairs the graph names that have the same open
    ///     time and trades (change add-ideas-and-market, spec market-history «Свечи названной пары»); a candle of a named
    ///     pair outside its coverage is not read, so it is not there. Null when the graph names no pair.
    /// </summary>
    public static IEnumerable<(ClosedCandle Candle, CandleFacts Facts, IReadOnlyDictionary<string, ClosedCandle>? Pairs)> WithPairs(
        TimeSpan timeframe,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named,
        IEnumerable<OpenInterestPoint>? openInterest = null)
    {
        Dictionary<DateTimeOffset, Dictionary<string, ClosedCandle>>? byOpen = named is null || named.Count == 0 ? null : ByOpen(timeframe, named);
        foreach ((ClosedCandle candle, CandleFacts facts) in Closed(timeframe, candles, funding, openInterest))
        {
            IReadOnlyDictionary<string, ClosedCandle>? pairs = byOpen is null
                ? null
                : byOpen.TryGetValue(candle.OpenTime, out Dictionary<string, ClosedCandle>? found) ? found : Empty;
            yield return (candle, facts, pairs);
        }
    }

    private static readonly IReadOnlyDictionary<string, ClosedCandle> Empty = new Dictionary<string, ClosedCandle>(StringComparer.Ordinal);

    private static Dictionary<DateTimeOffset, Dictionary<string, ClosedCandle>> ByOpen(TimeSpan timeframe, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>> named)
    {
        var byOpen = new Dictionary<DateTimeOffset, Dictionary<string, ClosedCandle>>();
        foreach ((string symbol, IReadOnlyList<HistoryCandle> candles) in named)
        {
            foreach (HistoryCandle candle in candles.Where(candle => candle.HasTrades))
            {
                if (!byOpen.TryGetValue(candle.OpenTime, out Dictionary<string, ClosedCandle>? pairs))
                {
                    pairs = new Dictionary<string, ClosedCandle>(StringComparer.Ordinal);
                    byOpen.Add(candle.OpenTime, pairs);
                }

                pairs[symbol] = candle.ToClosedCandle(timeframe);
            }
        }

        return byOpen;
    }
}
