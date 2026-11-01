using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>Limits and defaults of the service for new backtests.</summary>
/// <param name="MaxCombinations"></param>
/// <param name="DefaultFees">Base rates of the exchange by market category, for a backtest that gives none.</param>
public sealed record BacktestPolicy(long MaxCombinations, IReadOnlyDictionary<MarketCategory, FeeRates> DefaultFees);