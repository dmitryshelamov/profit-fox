using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetBacktest;

public sealed record GetBacktestResult(Backtest Backtest, bool BotChanged);