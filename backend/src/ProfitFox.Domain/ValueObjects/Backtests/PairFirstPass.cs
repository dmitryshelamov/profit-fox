namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What the first pass of a backtest of a category of ideas keeps of a set on a pair (change fix-backtest-random-control,
///     design.md, decision 4): the moments and sides of its orders in history, those the position will skip included, and the
///     close of the first candle of the timeframe of the set, which tells from which week the pair can be in a control group.
///     Null when the pair has no candle of the timeframe.
/// </summary>
public sealed record PairFirstPass(IReadOnlyList<PairOrder> Orders, DateTimeOffset? FirstClose);