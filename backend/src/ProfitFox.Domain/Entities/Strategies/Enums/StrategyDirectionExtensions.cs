using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.Entities.Strategies.Enums;

public static class StrategyDirectionExtensions
{
    /// <summary>A short position needs a derivative, so a short strategy trades only on <c>linear</c>.</summary>
    public static IReadOnlyList<MarketCategory> SupportedCategories(this StrategyDirection direction)
    {
        return direction == StrategyDirection.Long ? [MarketCategory.Spot, MarketCategory.Linear] : [MarketCategory.Linear];
    }
}