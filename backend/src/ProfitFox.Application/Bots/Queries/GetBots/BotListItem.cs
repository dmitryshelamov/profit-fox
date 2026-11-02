using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;

namespace ProfitFox.Application.Bots.Queries.GetBots;

/// <summary>A bot with its newest backtest, <c>null</c> when it has none.</summary>
public sealed record BotListItem(Bot Bot, Backtest? LastBacktest);