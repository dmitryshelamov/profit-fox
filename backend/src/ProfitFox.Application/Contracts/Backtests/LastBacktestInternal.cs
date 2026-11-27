namespace ProfitFox.Application.Contracts.Backtests;

/// <summary>
///     The last backtest of a strategy in its list (spec strategy-constructor «Последний прогон стратегии»): its id, state
///     and the version of the strategy it is of.
/// </summary>
public sealed record LastBacktestInternal(string Id, string Status, int Version);
