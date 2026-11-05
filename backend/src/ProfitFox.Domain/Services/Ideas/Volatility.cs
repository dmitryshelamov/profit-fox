using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>ATR / price of a pair on an interval and its 90th percentile, which «Высокая волатильность» exceeds.</summary>
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

    /// <summary>The 90th percentile by nearest rank; fewer than 100 ratios give none.</summary>
    public static decimal? Percentile(IReadOnlyList<decimal> ratios)
    {
        if (ratios.Count < MinimumRatios)
        {
            return null;
        }

        decimal[] sorted = [.. ratios.Order()];

        return sorted[(int)Math.Ceiling(sorted.Length * 0.9m) - 1];
    }
}