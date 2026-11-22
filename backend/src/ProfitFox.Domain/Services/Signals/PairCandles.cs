using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Signals;

/// <summary>
///     The candles of one pair as a graph gets them, a signal and a strategy alike (spec signal-preview): only the candles
///     with trades, in the order given, each with the last funding payment paid by its close.
/// </summary>
public static class PairCandles
{
    public static IEnumerable<(ClosedCandle Candle, FundingPayment? LastFunding)> Closed(
        TimeSpan timeframe,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding)
    {
        FundingPayment[] payments = [.. funding.Select(payment => payment.ToFundingPayment()).OrderBy(payment => payment.Time)];
        int paid = 0;
        foreach (HistoryCandle candle in candles.Where(candle => candle.HasTrades))
        {
            var closed = candle.ToClosedCandle(timeframe);
            while (paid < payments.Length && payments[paid].Time <= closed.CloseTime)
            {
                paid++;
            }

            yield return (closed, paid > 0 ? payments[paid - 1] : null);
        }
    }

    /// <summary>
    ///     The candles of <see cref="Closed" />, each with the candles of the pairs the graph names that have the same open
    ///     time and trades (change add-ideas-and-market, spec market-history «Свечи названной пары»); a candle of a named
    ///     pair outside its coverage is not read, so it is not there. Null when the graph names no pair.
    /// </summary>
    public static IEnumerable<(ClosedCandle Candle, FundingPayment? LastFunding, IReadOnlyDictionary<string, ClosedCandle>? Pairs)> WithPairs(
        TimeSpan timeframe,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named)
    {
        Dictionary<DateTimeOffset, Dictionary<string, ClosedCandle>>? byOpen = named is null || named.Count == 0 ? null : ByOpen(timeframe, named);
        foreach ((ClosedCandle candle, FundingPayment? lastFunding) in Closed(timeframe, candles, funding))
        {
            IReadOnlyDictionary<string, ClosedCandle>? pairs = byOpen is null
                ? null
                : byOpen.TryGetValue(candle.OpenTime, out Dictionary<string, ClosedCandle>? found) ? found : Empty;
            yield return (candle, lastFunding, pairs);
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
