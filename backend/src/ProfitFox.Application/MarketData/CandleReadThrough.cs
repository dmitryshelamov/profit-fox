using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData;

/// <summary>The candles of a period read from storage; <see cref="Unavailable" /> is set when the exchange could not fill what was missing.</summary>
public sealed record CandleReadThroughResult(IReadOnlyList<Candle> Candles, bool Unavailable);

/// <summary>
///     Reads a period of closed candles from storage and first fills what is missing from the exchange with one request, a single
///     attempt. It is not a candle import: it takes no lease and waits for no queue, and it stores only closed candles that were
///     missing, so the rows an import writes are never changed.
/// </summary>
public sealed class CandleReadThrough(ICandleRepository candles, IMarketHistoryClient history, TimeProvider time)
{
    // A period is at most this many candles, which one page of the exchange covers.
    private const int MaxCandles = 1000;

    // The exchange may still serve a candle unfinished for a moment after its close; one stored that way would stay wrong.
    public static readonly TimeSpan CloseMargin = TimeSpan.FromSeconds(5);

    public async Task<CandleReadThroughResult> ReadAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DateTimeOffset> stored = await candles.GetOpenTimesAsync(instrument, interval, from, to, cancellationToken);
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> missing = interval.MissingRanges(stored, from, to);
        bool unavailable = false;

        if (missing.Count > 0)
        {
            try
            {
                IReadOnlyList<Candle> fetched = await history.GetCandlesOnceAsync(instrument, interval, missing[0].From, missing[^1].To, cancellationToken);
                DateTimeOffset now = time.GetUtcNow();
                Candle[] gaps = [.. fetched.Where(candle => candle.IsClosedAt(interval, now - CloseMargin) && missing.Any(range => candle.OpenTime >= range.From && candle.OpenTime < range.To))];

                if (gaps.Length > 0)
                {
                    await candles.UpsertAsync(instrument, interval, gaps, cancellationToken);
                }
            }
            catch (ExchangeException)
            {
                unavailable = true;
            }
        }

        return new CandleReadThroughResult(await candles.GetAsync(instrument, interval, from, to, MaxCandles, cancellationToken), unavailable);
    }
}