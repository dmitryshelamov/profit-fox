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
    ///     History from the start of the data to <paramref name="end" />, as a backtest with that end reads it. The hash is
    ///     not needed, but the reader wants one.
    /// </summary>
    public static async Task<PreviewHistory> ReadAsync(IHistorySource source, string instrument, TimeSpan timeframe, DateTimeOffset end)
    {
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, end, hash);
        HistoryRead<HistoryCandle> candles = await history.Candles(instrument, timeframe, HistoryPeriod.DataStart, end, CancellationToken.None);
        HistoryRead<HistoryPayment> funding = await history.FundingThrough(instrument, HistoryPeriod.DataStart, end, CancellationToken.None);
        return new PreviewHistory(candles, funding);
    }
}
