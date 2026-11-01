using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.Backtests;

/// <summary>What one combination of a backtest gave; <see cref="ParameterValues" /> holds every parameter of the strategy.</summary>
public sealed record BacktestResult(
    Guid BacktestId,
    long Combination,
    IReadOnlyDictionary<string, NodeValue> ParameterValues,
    BacktestMetrics Metrics,
    TimeSpan CalculationTime);