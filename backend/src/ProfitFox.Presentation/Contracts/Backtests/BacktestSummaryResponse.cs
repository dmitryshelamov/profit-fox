using System.Text.Json.Nodes;

using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

public sealed record BacktestSummaryResponse(
    Guid Id,
    int StrategyVersion,
    DateTimeOffset From,
    DateTimeOffset To,
    JsonObject Parameters,
    string Status,
    string? Error,
    ProgressResponse Progress,
    DateTimeOffset CreatedAt,
    bool BotChanged)
{
    public static BacktestSummaryResponse Of(Backtest backtest, bool botChanged)
    {
        return new BacktestSummaryResponse(
            backtest.Id,
            backtest.StrategyVersion,
            backtest.From,
            backtest.To,
            BacktestJson.WriteParameters(backtest.Parameters),
            backtest.Status.ToCode(),
            backtest.Error,
            ProgressResponse.Of(backtest),
            backtest.CreatedAt,
            botChanged);
    }
}