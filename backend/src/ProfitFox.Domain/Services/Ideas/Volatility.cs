using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     ATR / price of a pair on an interval and its percentiles: «Высокая волатильность» exceeds the 90th, «Сжатие» stays below the 10th.
/// </summary>
public static class Volatility
{
    private const int Window = 220;
    private const int MinimumRatios = 100;

    /// <summary>After each candle from the 14th, oldest first: ATR(14) of the candles up to it, at most the last 220, over its close.</summary>
    public static IReadOnlyList<decimal> Ratios(IReadOnlyList<IdeaCandle> candles)
    {
        var ratios = new List<decimal>();

        for (int count = 1; count <= candles.Count; count++)
        {
            int from = Math.Max(0, count - Window);

            if (Atr.Compute([.. candles.Skip(from).Take(count - from)]) is { } atr)
            {
                ratios.Add(atr / candles[count - 1].Close);
            }
        }

        return ratios;
    }

    /// <summary>The percentile of <paramref name="rank" /> (0.1 for the 10th) by nearest rank; fewer than 100 ratios give none.</summary>
    public static decimal? Percentile(IReadOnlyList<decimal> ratios, decimal rank)
    {
        if (ratios.Count < MinimumRatios)
        {
            return null;
        }

        decimal[] sorted = [.. ratios.Order()];

        return Rank(sorted, rank);
    }

    /// <summary>The 10th and the 90th percentiles of the same ratios, sorted once; none with fewer than 100 ratios.</summary>
    public static VolatilityPercentiles? Percentiles(IReadOnlyList<decimal> ratios)
    {
        if (ratios.Count < MinimumRatios)
        {
            return null;
        }

        decimal[] sorted = [.. ratios.Order()];

        return new VolatilityPercentiles(Rank(sorted, 0.1m), Rank(sorted, 0.9m));
    }

    // Nearest rank: the value at the ceiling of the rank of the count.
    private static decimal Rank(decimal[] sorted, decimal rank)
    {
        return sorted[(int)Math.Ceiling(sorted.Length * rank) - 1];
    }
}