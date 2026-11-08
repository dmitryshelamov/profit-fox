using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies.Enums;

public class StrategyDirectionTests
{
    // Decision 9: a long strategy trades on every market; a short one needs a derivative, and the margin of MOEX is a later change.
    [Test]
    public void Long_strategy_trades_on_spot_linear_and_moex()
    {
        StrategyDirection.Long.SupportedCategories().ShouldBe([MarketCategory.Spot, MarketCategory.Linear, MarketCategory.Moex]);
    }

    // Scenarios "Шорт на споте" and "Шорт на MOEX".
    [Test]
    public void Short_strategy_trades_only_on_linear()
    {
        StrategyDirection.Short.SupportedCategories().ShouldBe([MarketCategory.Linear]);
    }
}