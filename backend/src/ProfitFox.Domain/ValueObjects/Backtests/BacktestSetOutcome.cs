namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A set of a backtest with its numbers over every pair, which only a done backtest has.
/// </summary>
public sealed record BacktestSetOutcome(BacktestSet Set, SetSummary? Numbers);
