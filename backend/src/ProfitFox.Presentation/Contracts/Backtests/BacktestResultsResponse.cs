namespace ProfitFox.Presentation.Contracts.Backtests;

/// <param name="Total">Results the backtest has so far.</param>
/// <param name="Items"></param>
public sealed record BacktestResultsResponse(long Total, IReadOnlyList<BacktestResultResponse> Items);