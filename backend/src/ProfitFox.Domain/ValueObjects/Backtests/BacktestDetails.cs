namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>What a combination did, for a review and charts: computed again on request by the same replay as the metrics.</summary>
public sealed record BacktestDetails(BacktestMetrics Metrics, IReadOnlyList<BacktestPosition> Positions, IReadOnlyList<EquityPoint> Equity);