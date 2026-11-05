using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The closed candles of a pair on an interval, at most 220, on the grid of the interval in UTC (decision 2), and the minutes of the
///     candle that forms. It warms up from stored candles and grows by closed minutes without gaps: the last minute of a candle closes it.
/// </summary>
public sealed class IdeaSeries(Interval interval)
{
    public const int Capacity = 220;

    private readonly List<IdeaCandle> _closed = [];
    private readonly List<IdeaCandle> _forming = [];

    public Interval Interval => interval;

    public IReadOnlyList<IdeaCandle> Closed => _closed;

    /// <param name="closed">The closed candles, oldest first.</param>
    /// <param name="forming">The minutes of the candle after them, oldest first.</param>
    public void Warm(IReadOnlyList<IdeaCandle> closed, IReadOnlyList<IdeaCandle> forming)
    {
        _closed.Clear();
        _closed.AddRange(closed.TakeLast(Capacity));
        _forming.Clear();
        _forming.AddRange(forming);
    }

    public void Add(IdeaCandle minute)
    {
        _forming.Add(minute);
        DateTimeOffset openTime = interval.CandleOpen(minute.OpenTime);

        if (minute.OpenTime.AddMinutes(1) < interval.CandleEnd(openTime))
        {
            return;
        }

        _closed.Add(new IdeaCandle(
            openTime,
            _forming[0].Open,
            _forming.Max(candle => candle.High),
            _forming.Min(candle => candle.Low),
            minute.Close,
            _forming.Sum(candle => candle.Volume)));
        _forming.Clear();

        if (_closed.Count > Capacity)
        {
            _closed.RemoveAt(0);
        }
    }

    /// <summary>
    ///     The closed candles a setup on the minute that ends at <paramref name="end" /> is measured against: the ones that end before the window
    ///     of its last candle, the last minutes of the interval's length.
    /// </summary>
    public IReadOnlyList<IdeaCandle> Base(DateTimeOffset end)
    {
        TimeSpan length = interval.CandleEnd(end) - end;
        DateTimeOffset windowStart = end - length;
        int count = _closed.Count;

        while (count > 0 && interval.CandleEnd(_closed[count - 1].OpenTime) > windowStart)
        {
            count--;
        }

        return _closed[..count];
    }
}