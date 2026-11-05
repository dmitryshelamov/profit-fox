using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     What the API measures a pair by on an interval (decision 8), with the same functions as the scanner: the price and the spread of its
///     row of the snapshot, ATR(14) and the zones of the closed candles joined from storage before the window of the last candle, and the low and
///     high of that window, the last minutes of the interval's length. The API has no tie to the scanner.
/// </summary>
public sealed record IdeaLevels(decimal Price, decimal? Spread, decimal? Atr, IReadOnlyList<Zone> Zones, decimal WindowLow, decimal WindowHigh)
{
    public static async Task<IdeaLevels> ReadAsync(ICandleRepository candles, MarketSnapshotRow row, Interval interval, IdeasSettings settings, CancellationToken cancellationToken)
    {
        // The snapshot is computed a few seconds after the end of its minute.
        DateTimeOffset end = Interval.OneMinute.CandleOpen(row.ComputedAt);
        DateTimeOffset to = interval.CandleOpen(end);
        TimeSpan length = interval.CandleEnd(to) - to;
        var series = new IdeaSeries(interval);
        series.Warm(await candles.GetAggregatedAsync(row.Instrument, interval, to - (length * IdeaSeries.Capacity), to, cancellationToken), []);
        IReadOnlyList<IdeaCandle> closed = series.Base(end);
        decimal price = row.Metrics.Price!.Value;
        IReadOnlyList<Candle> stored = await candles.GetAsync(row.Instrument, Interval.OneMinute, end - length, end, (int)length.TotalMinutes, cancellationToken);
        IReadOnlyList<IdeaCandle> window = IdeaCandles.Minutes(
            [.. stored.Select(candle => new IdeaCandle(candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Turnover.Value))],
            end - length,
            end,
            price);
        decimal? atr = Domain.Services.Ideas.Atr.Compute(closed);

        return new IdeaLevels(
            price,
            row.Metrics.Spread * price / 100m,
            atr,
            atr is { } value ? ZoneFinder.Find(closed, value, new ZoneRules(settings.ZoneMerge, settings.ZoneStrength)) : [],
            window.Min(minute => minute.Low),
            window.Max(minute => minute.High));
    }
}