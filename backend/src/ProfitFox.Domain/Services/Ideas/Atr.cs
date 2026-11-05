using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>The average true range of Wilder over the candles of an interval, oldest first.</summary>
public static class Atr
{
    /// <summary>
    ///     The mean of the first <paramref name="period" /> true ranges, then smoothed by every next one: (ATR × (period − 1) + range) /
    ///     period. The first candle's true range is its high minus its low; fewer candles than the period give no value.
    /// </summary>
    public static decimal? Compute(IReadOnlyList<IdeaCandle> candles, int period = 14)
    {
        if (candles.Count < period)
        {
            return null;
        }

        decimal atr = Enumerable.Range(0, period).Sum(index => TrueRange(candles, index)) / period;

        for (int index = period; index < candles.Count; index++)
        {
            atr = ((atr * (period - 1)) + TrueRange(candles, index)) / period;
        }

        return atr;
    }

    private static decimal TrueRange(IReadOnlyList<IdeaCandle> candles, int index)
    {
        IdeaCandle candle = candles[index];

        if (index == 0)
        {
            return candle.High - candle.Low;
        }

        decimal close = candles[index - 1].Close;

        return Math.Max(candle.High - candle.Low, Math.Max(Math.Abs(candle.High - close), Math.Abs(candle.Low - close)));
    }
}