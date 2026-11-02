using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Application.Bots.Queries.GetBots;

/// <summary>A bot with its newest backtest and its exchange account, each <c>null</c> when it has none.</summary>
public sealed record BotListItem(Bot Bot, Backtest? LastBacktest, ExchangeAccount? Account);