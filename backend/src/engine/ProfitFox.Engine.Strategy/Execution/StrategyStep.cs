using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     What a strategy gives at a closed candle of a pair (spec strategy-execution): the close of the candle, the side of
///     the entry order or null without one, and the values of the exit nodes at this candle — the time of
///     <c>close-after</c>, the stop and the target. The values are given at every candle; an order carries those of its
///     candle.
/// </summary>
public sealed record StrategyStep(DateTimeOffset Moment, EntrySide? Entry, TimeSpan? After, ExitDistance Stop, ExitDistance Target);
