using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Execution;

/// <summary>
///     What a strategy gives at a closed candle of a pair (spec strategy-execution): the close of the candle, the side of
///     the entry order or null without one, and the values of the exit nodes at this candle — the time of
///     <c>close-after</c>, the stop and the target. The values are given at every candle; an order carries those of its
///     candle. <see cref="Size" /> is the size of the entry in USDT (change add-position-node): that of the node of the
///     order, or of the first <c>enter</c> without one; null when it is missing, <see cref="DefaultSize" /> unless given.
/// </summary>
public sealed record StrategyStep(DateTimeOffset Moment, EntrySide? Entry, TimeSpan? After, ExitDistance Stop, ExitDistance Target)
{
    /// <summary>
    ///     The size of the entry of the strategy of a hypothesis and of a strategy lifted from the form before change
    ///     add-position-node.
    /// </summary>
    public const decimal DefaultSize = 100m;

    public decimal? Size { get; init; } = DefaultSize;
}
