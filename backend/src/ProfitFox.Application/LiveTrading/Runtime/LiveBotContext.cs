using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>What a session needs of a bot for the whole time it keeps it: its settings, the strategy version it started on, the key.</summary>
internal sealed record LiveBotContext(
    Bot Bot,
    StrategySnapshot Strategy,
    TradingConditions Conditions,
    ExchangeKey Key,
    string BaseCoin)
{
    public StrategyDirection Direction => Strategy.Definition.Direction;
}