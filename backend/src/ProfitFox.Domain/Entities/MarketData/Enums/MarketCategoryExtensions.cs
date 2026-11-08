using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Domain.Entities.MarketData.Enums;

public static class MarketCategoryExtensions
{
    /// <summary>The exchange a market is on: it follows from the category and is stored nowhere.</summary>
    public static Exchange Exchange(this MarketCategory category)
    {
        return category == MarketCategory.Moex ? ExchangeAccounts.Enums.Exchange.TInvest : ExchangeAccounts.Enums.Exchange.Bybit;
    }
}