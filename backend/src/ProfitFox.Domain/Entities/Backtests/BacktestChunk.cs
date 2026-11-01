namespace ProfitFox.Domain.Entities.Backtests;

/// <summary>
///     Combinations <c>[First, First + Combinations)</c> of a backtest that one Worker computes under one lease. <see cref="LostLeases" />
///     counts leases in a row that expired on the chunk before it saved a result.
/// </summary>
public sealed record BacktestChunk(Guid BacktestId, int Number, long First, int Combinations, int LostLeases = 0)
{
    // A chunk that keeps stopping its Worker before it saves anything would otherwise be taken over forever.
    public const int MaxLostLeases = 3;

    /// <summary>Consecutive chunks of <paramref name="size" /> combinations over the whole grid; the last one may be shorter.</summary>
    public static IReadOnlyList<BacktestChunk> Split(Guid backtestId, long combinations, int size)
    {
        return
        [
            .. Enumerable.Range(0, (int)((combinations + size - 1) / size))
                .Select(number => new BacktestChunk(backtestId, number, (long)number * size, (int)Math.Min(size, combinations - ((long)number * size))))
        ];
    }
}