namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>Fee rates of limit and market orders in percent.</summary>
public sealed record FeeRates(decimal LimitPercent, decimal MarketPercent);