using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;

namespace ProfitFox.Presentation.Contracts.Bots;

/// <summary>The balance is a decimal string: JSON numbers lose precision in JavaScript clients.</summary>
public sealed record BotResponse(
    Guid Id,
    string Name,
    Guid StrategyId,
    JsonObject ParameterValues,
    string Category,
    string Symbol,
    string Interval,
    string Balance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BotResponse Of(Bot bot)
    {
        return new BotResponse(
            bot.Id,
            bot.Name,
            bot.StrategyId,
            StrategyJson.WriteValues(bot.ParameterValues),
            bot.Instrument.Category.ToCode(),
            bot.Instrument.Symbol,
            bot.Interval.ToCode(),
            bot.Balance.ToString(CultureInfo.InvariantCulture),
            bot.CreatedAt,
            bot.UpdatedAt);
    }
}