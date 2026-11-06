using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     RSI(14) of Wilder over exactly the last 100 closed candles of an interval (decision 3): the mean gain and loss of the first 14 changes,
///     then smoothed by every next one. Wilder's value depends on the candle the count starts at, so the fixed window makes a warm-up after a
///     restart and a replay agree.
/// </summary>
public static class Rsi
{
    public const int Candles = 100;
    private const int Period = 14;

    /// <summary>The smoothing after the last 100 candles, oldest first; fewer give none.</summary>
    public static RsiState? Smooth(IReadOnlyList<IdeaCandle> closed)
    {
        if (closed.Count < Candles)
        {
            return null;
        }

        IdeaCandle[] candles = [.. closed.TakeLast(Candles)];
        decimal[] changes = [.. Enumerable.Range(1, Period).Select(index => candles[index].Close - candles[index - 1].Close)];
        var state = new RsiState(changes.Sum(change => Math.Max(change, 0m)) / Period, changes.Sum(change => Math.Max(-change, 0m)) / Period, candles[Period].Close);

        return candles.Skip(Period + 1).Aggregate(state, (current, candle) => current.Next(candle.Close));
    }
}