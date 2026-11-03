using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>The closed candles of the exchange history a live bot warms up its indicators with.</summary>
internal sealed class LiveWarmUpHistory(IMarketHistoryClient history, TimeProvider time)
{
    // The most closed candles one request of the exchange gives, which the indicators are warmed up with.
    private const int WarmUpCandles = 1000;

    /// <summary>The last closed candles of the instrument and interval, oldest first, each once.</summary>
    public async Task<Candle[]> ClosedCandlesAsync(Instrument instrument, Interval interval, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        // A Monday at midnight opens a candle of every interval; a month is taken at its longest.
        DateTimeOffset aligned = DateTimeOffset.UnixEpoch.AddDays(4);
        TimeSpan length = interval.CandleEnd(aligned) - aligned;
        var candles = new List<Candle>();

        await foreach (IReadOnlyList<Candle> page in history.GetCandlePagesAsync(instrument, interval, now - length * WarmUpCandles, now, cancellationToken))
        {
            candles.AddRange(page.Where(candle => candle.IsClosedAt(interval, now)));
        }

        return [.. candles.DistinctBy(candle => candle.OpenTime).OrderBy(candle => candle.OpenTime).TakeLast(WarmUpCandles)];
    }
}