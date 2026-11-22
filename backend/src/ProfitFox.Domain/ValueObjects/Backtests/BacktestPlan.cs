using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A set as a pair computes it: its number, the strategy with the values of the set and its design.
/// </summary>
public sealed record BacktestPlan(int Set, TradingStrategy Strategy, BacktestDesign Design);
