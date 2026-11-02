using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Application.Bots.Queries.GetBot;

/// <summary>A bot with the exchange account it trades with, <c>null</c> when it has none.</summary>
public sealed record GetBotResult(Bot Bot, ExchangeAccount? Account);