namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     A set of a backtest and its pairs with orders, by the sum of their results from the highest (spec strategy-backtest
///     «Результат по парам»).
/// </summary>
public sealed record BacktestSetPairsResponse(BacktestSetResponse Set, IReadOnlyList<BacktestPairResponse> Pairs);
