namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     Fee rates of limit and market orders in percent, and the slippage in percent the ideas count on every fill that does not rest on the
///     book; a backtest takes its slippage from its own settings.
/// </summary>
public sealed record FeeRates(decimal LimitPercent, decimal MarketPercent, decimal SlippagePercent = 0m);