using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.LiveTrading;

namespace ProfitFox.Application.Bots.Queries.GetBots;

/// <summary>A bot with its newest backtest and its exchange account, each <c>null</c> when it has none, and where it is in live trading.</summary>
public sealed record BotListItem(Bot Bot, Backtest? LastBacktest, ExchangeAccount? Account, LiveBot Live);