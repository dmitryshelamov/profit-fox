using ProfitFox.Domain.Entities.Backtests.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>How a backtest simulates the exchange; fees and slippage are in percent (<c>0.1</c> is 0.1 %).</summary>
public sealed record SimulationSettings(decimal Balance, decimal LimitFeePercent, decimal MarketFeePercent, decimal SlippagePercent, LimitFillMode LimitFillMode);