using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     Zones of support and resistance from the last 200 closed candles of an interval. A touch is a high above the two candles before it and
///     not below the two after it, or a low the other way round, so a flat top counts once. Touches go up from the lowest: one within
///     <see cref="ZoneRules.Merge" /> ATR of the lowest touch of a zone joins it.
/// </summary>
public static class ZoneFinder
{
    private const int Candles = 200;
    private const int Side = 2;

    public static IReadOnlyList<Zone> Find(IReadOnlyList<IdeaCandle> closed, decimal atr, ZoneRules rules)
    {
        IdeaCandle[] candles = [.. closed.TakeLast(Candles)];
        var touches = new List<decimal>();

        for (int index = Side; index < candles.Length - Side; index++)
        {
            if (Extreme(candles, index, candle => candle.High))
            {
                touches.Add(candles[index].High);
            }

            if (Extreme(candles, index, candle => -candle.Low))
            {
                touches.Add(candles[index].Low);
            }
        }

        var zones = new List<Zone>();

        foreach (decimal touch in touches.Order())
        {
            if (zones.Count > 0 && touch - zones[^1].Low <= rules.Merge * atr)
            {
                zones[^1] = zones[^1] with { High = touch, Strength = zones[^1].Strength + 1 };
            }
            else
            {
                zones.Add(new Zone(touch, touch, 1));
            }
        }

        return [.. zones.Where(zone => zone.Strength >= rules.Strength)];
    }

    /// <summary>The nearest zone wholly below the price.</summary>
    public static Zone? Support(IReadOnlyList<Zone> zones, decimal price)
    {
        return zones.Where(zone => zone.High < price).MaxBy(zone => zone.High);
    }

    /// <summary>The nearest zone wholly above the price.</summary>
    public static Zone? Resistance(IReadOnlyList<Zone> zones, decimal price)
    {
        return zones.Where(zone => zone.Low > price).MinBy(zone => zone.Low);
    }

    private static bool Extreme(IdeaCandle[] candles, int index, Func<IdeaCandle, decimal> value)
    {
        decimal at = value(candles[index]);

        return Enumerable.Range(1, Side).All(offset => at > value(candles[index - offset]) && at >= value(candles[index + offset]));
    }
}