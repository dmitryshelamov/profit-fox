using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The history of one pair and timeframe over exploration that a preview reads: the candles with their coverage and
///     the funding payments. The previews of a signal and of a strategy read it the same way and share it in
///     <see cref="PreviewHistoryCache" />.
/// </summary>
public sealed record PreviewHistory(HistoryRead<HistoryCandle> Candles, HistoryRead<HistoryPayment> Funding)
{
    /// <summary>
    ///     History from the start of the data to <paramref name="end" />, as a backtest with that end reads it on the clock
    ///     <paramref name="time" />. The hash is not needed, but the reader wants one.
    /// </summary>
    public static async Task<PreviewHistory> ReadAsync(IHistorySource source, string instrument, TimeSpan timeframe, DateTimeOffset end, TimeProvider time)
    {
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, end, hash, time);
        HistoryRead<HistoryCandle> candles = await history.Candles(instrument, timeframe, HistoryPeriod.DataStart, end, CancellationToken.None);
        HistoryRead<HistoryPayment> funding = await history.FundingThrough(instrument, HistoryPeriod.DataStart, end, CancellationToken.None);
        return new PreviewHistory(candles, funding);
    }

    /// <summary>
    ///     The candles of the pairs a graph names on <paramref name="timeframe" /> up to <paramref name="end" />, by symbol,
    ///     each read once through <paramref name="cache" /> (change add-ideas-and-market, spec market-history «Свечи
    ///     названной пары»); null when the graph names none. The pairs were checked against <paramref name="listed" />.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>?> NamedAsync(
        IReadOnlyList<string> named,
        IReadOnlyList<Instrument> listed,
        PreviewHistoryCache cache,
        IHistorySource source,
        TimeSpan timeframe,
        DateTimeOffset end,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (named.Count == 0)
        {
            return null;
        }

        var candles = new Dictionary<string, IReadOnlyList<HistoryCandle>>(StringComparer.Ordinal);
        foreach (Instrument pair in listed.Where(pair => named.Contains(pair.Symbol, StringComparer.Ordinal)))
        {
            PreviewHistory history = await cache.Get(pair.Id, timeframe, end, () => ReadAsync(source, pair.Id, timeframe, end, time), cancellationToken);
            candles[pair.Symbol] = history.Candles.Items;
        }

        return candles;
    }
}
