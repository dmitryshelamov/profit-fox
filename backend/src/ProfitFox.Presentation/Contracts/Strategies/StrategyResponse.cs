using System.Text.Json.Nodes;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Presentation.Contracts.Strategies;

public sealed record StrategyResponse(
    Guid Id,
    string Name,
    string Direction,
    IReadOnlyList<string> SupportedCategories,
    JsonObject Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static StrategyResponse Of(Strategy strategy)
    {
        StrategyDirection direction = strategy.Definition.Direction;

        return new StrategyResponse(
            strategy.Id,
            strategy.Name,
            StrategyCodes.ToCode(direction),
            [.. direction.SupportedCategories().Select(category => category.ToCode())],
            StrategyJson.WriteStrategy(strategy.Definition),
            strategy.CreatedAt,
            strategy.UpdatedAt);
    }
}