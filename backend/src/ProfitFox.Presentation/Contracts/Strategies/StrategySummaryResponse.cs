using ProfitFox.Application.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Presentation.Contracts.Strategies;

public sealed record StrategySummaryResponse(Guid Id, string Name, string Direction, IReadOnlyList<string> SupportedCategories)
{
    public static StrategySummaryResponse Of(Strategy strategy)
    {
        StrategyDirection direction = strategy.Definition.Direction;

        return new StrategySummaryResponse(
            strategy.Id,
            strategy.Name,
            StrategyCodes.ToCode(direction),
            [.. direction.SupportedCategories().Select(category => category.ToCode())]);
    }
}