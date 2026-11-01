using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

public sealed record BacktestSummaryResponse(
    Guid Id,
    int StrategyVersion,
    DateTimeOffset From,
    DateTimeOffset To,
    string Status,
    ProgressResponse Progress,
    DateTimeOffset CreatedAt)
{
    public static BacktestSummaryResponse Of(Backtest backtest)
    {
        return new BacktestSummaryResponse(
            backtest.Id,
            backtest.StrategyVersion,
            backtest.From,
            backtest.To,
            backtest.Status.ToCode(),
            ProgressResponse.Of(backtest),
            backtest.CreatedAt);
    }
}