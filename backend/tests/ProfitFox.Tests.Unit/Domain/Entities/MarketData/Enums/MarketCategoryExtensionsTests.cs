using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.MarketData.Enums;

public class MarketCategoryExtensionsTests
{
    // Decision 1: the exchange of an instrument follows from its market category.
    [TestCase(MarketCategory.Spot, Exchange.Bybit)]
    [TestCase(MarketCategory.Linear, Exchange.Bybit)]
    [TestCase(MarketCategory.Moex, Exchange.TInvest)]
    public void Exchange_follows_from_the_category(MarketCategory category, Exchange exchange)
    {
        category.Exchange().ShouldBe(exchange);
    }

    // Decision 4: only a market with sessions keeps the border of its coverage; crypto has no nights.
    [TestCase(MarketCategory.Spot, true)]
    [TestCase(MarketCategory.Linear, true)]
    [TestCase(MarketCategory.Moex, false)]
    public void Only_moex_has_sessions(MarketCategory category, bool aroundTheClock)
    {
        category.TradesAroundTheClock().ShouldBe(aroundTheClock);
    }
}