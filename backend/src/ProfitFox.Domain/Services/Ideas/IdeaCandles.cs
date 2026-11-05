using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     Candles of an interval from minute candles, on the grid of the interval in UTC as the exchange draws them. A minute of the covered
///     period without a candle had no trades: it is flat at the close before it with no volume.
/// </summary>
public static class IdeaCandles
{
    /// <summary>
    ///     The candles that lie whole in <c>[from, to)</c>, every minute of which is covered; <paramref name="minutes" /> are oldest first and
    ///     may start before <paramref name="from" />, the last of those gives the close before the period.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> FromMinutes(Interval interval, IReadOnlyList<IdeaCandle> minutes, DateTimeOffset from, DateTimeOffset to)
    {
        decimal? closeBefore = minutes.LastOrDefault(minute => minute.OpenTime < from)?.Close;
        MinuteAggregate[] aggregates =
        [
            .. minutes
                .Where(minute => minute.OpenTime >= from && minute.OpenTime < to)
                .GroupBy(minute => interval.CandleOpen(minute.OpenTime))
                .Select(group => new MinuteAggregate(
                    group.Key,
                    group.First().OpenTime,
                    group.First().Open,
                    group.Max(minute => minute.High),
                    group.Min(minute => minute.Low),
                    group.Last().Close,
                    group.Sum(minute => minute.Volume)))
        ];

        return Join(interval, aggregates, from, to, closeBefore);
    }

    /// <summary>
    ///     The candles of <c>[from, to)</c> from the joined minutes with trades of each, oldest first. A gap inside a candle cannot move
    ///     its high or low, since its price is the close of a minute of the same candle; a gap at its start opens it at the close before it,
    ///     and a candle without trades is flat at that close. Without a close before, a gap at the start is left out.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> Join(
        Interval interval,
        IReadOnlyList<MinuteAggregate> aggregates,
        DateTimeOffset from,
        DateTimeOffset to,
        decimal? closeBefore)
    {
        var byOpenTime = aggregates.ToDictionary(aggregate => aggregate.OpenTime);
        var candles = new List<IdeaCandle>();
        DateTimeOffset openTime = interval.CandleOpen(from);

        if (openTime < from)
        {
            openTime = interval.CandleEnd(openTime);
        }

        for (; interval.CandleEnd(openTime) <= to; openTime = interval.CandleEnd(openTime))
        {
            IdeaCandle candle;

            if (byOpenTime.TryGetValue(openTime, out MinuteAggregate? aggregate))
            {
                candle = aggregate.FirstMinute > openTime && closeBefore is { } close
                    ? new IdeaCandle(openTime, close, Math.Max(aggregate.High, close), Math.Min(aggregate.Low, close), aggregate.Close, aggregate.Volume)
                    : new IdeaCandle(openTime, aggregate.Open, aggregate.High, aggregate.Low, aggregate.Close, aggregate.Volume);
            }
            else if (closeBefore is { } close)
            {
                candle = new IdeaCandle(openTime, close, close, close, close, 0m);
            }
            else
            {
                continue;
            }

            candles.Add(candle);
            closeBefore = candle.Close;
        }

        return candles;
    }

    /// <summary>
    ///     Every minute of the covered period <c>[from, to)</c>: the known ones as they are, the others flat at the close before them, the
    ///     first of those at <paramref name="closeBefore" />. Without a close before, the minutes start at the first one known.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> Minutes(IReadOnlyList<IdeaCandle> known, DateTimeOffset from, DateTimeOffset to, decimal? closeBefore)
    {
        var byOpenTime = known.Where(minute => minute.OpenTime >= from && minute.OpenTime < to).ToDictionary(minute => minute.OpenTime);
        var minutes = new List<IdeaCandle>();

        for (DateTimeOffset openTime = from; openTime < to; openTime = openTime.AddMinutes(1))
        {
            IdeaCandle? minute = byOpenTime.GetValueOrDefault(openTime) ?? (closeBefore is { } close ? new IdeaCandle(openTime, close, close, close, close, 0m) : null);

            if (minute is not null)
            {
                minutes.Add(minute);
                closeBefore = minute.Close;
            }
        }

        return minutes;
    }
}