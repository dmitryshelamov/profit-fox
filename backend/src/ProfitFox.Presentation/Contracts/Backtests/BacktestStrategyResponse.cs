namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The strategy of a backtest as it was when the backtest was queued: its id, name, version and kind, <c>bot</c> or
///     <c>idea</c>.
/// </summary>
public sealed record BacktestStrategyResponse(Guid Id, string Name, int Version, string Kind);
