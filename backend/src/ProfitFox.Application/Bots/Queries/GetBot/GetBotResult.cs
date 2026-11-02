using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.LiveTrading;

namespace ProfitFox.Application.Bots.Queries.GetBot;

/// <summary>A bot with the exchange account it trades with, <c>null</c> when it has none, and where it is in live trading.</summary>
public sealed record GetBotResult(Bot Bot, ExchangeAccount? Account, LiveBot Live);