using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetBacktestResults;

/// <param name="Total">Results the backtest has so far.</param>
/// <param name="Items"></param>
public sealed record GetBacktestResultsResult(long Total, IReadOnlyList<BacktestResult> Items);