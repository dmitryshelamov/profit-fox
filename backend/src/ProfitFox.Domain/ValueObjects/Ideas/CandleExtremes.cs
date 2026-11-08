namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The high and the low of the last 200 closed candles of an interval, which «Новый максимум / минимум» is read against (decision 1 of
///     speed-up-ideas-replay): measured once as the closed candles change, not every minute; the first of their value, as Max and Min take them.
/// </summary>
public sealed record CandleExtremes(decimal High, decimal Low)
{
    public const int Candles = 200;

    /// <summary>The extremes of the last 200 of <paramref name="closed" />; none with fewer.</summary>
    public static CandleExtremes? Of(IReadOnlyList<IdeaCandle> closed)
    {
        if (closed.Count < Candles)
        {
            return null;
        }

        int first = closed.Count - Candles;
        decimal high = closed[first].High;
        decimal low = closed[first].Low;

        for (int index = first + 1; index < closed.Count; index++)
        {
            IdeaCandle candle = closed[index];

            if (candle.High > high)
            {
                high = candle.High;
            }

            if (candle.Low < low)
            {
                low = candle.Low;
            }
        }

        return new CandleExtremes(high, low);
    }
}