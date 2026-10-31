using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies.Enums;

public class StrategyDirectionTests
{
    [Test]
    public void Long_strategy_trades_on_spot_and_linear()
    {
        StrategyDirection.Long.SupportedCategories().ShouldBe([MarketCategory.Spot, MarketCategory.Linear]);
    }

    [Test]
    public void Short_strategy_trades_only_on_linear()
    {
        StrategyDirection.Short.SupportedCategories().ShouldBe([MarketCategory.Linear]);
    }
}