using ProfitFox.Domain.Services.Backtests;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>What a combination did, for a review and charts: computed again on request by the same replay as the metrics.</summary>
public sealed record BacktestDetails(BacktestMetrics Metrics, IReadOnlyList<ReplayFill> Fills, IReadOnlyList<BacktestTrade> Trades, IReadOnlyList<EquityPoint> Equity);