using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Domain.Entities.MarketData.Enums;

public static class MarketCategoryExtensions
{
    /// <summary>The exchange a market is on: it follows from the category and is stored nowhere.</summary>
    public static Exchange Exchange(this MarketCategory category)
    {
        return category == MarketCategory.Moex ? ExchangeAccounts.Enums.Exchange.TInvest : ExchangeAccounts.Enums.Exchange.Bybit;
    }

    /// <summary>
    ///     Whether the market trades every minute of every day, so a candle missing from a series is a gap the exchange can still fill.
    ///     A market with sessions (MOEX) has nights and weekends without candles, which only the border of the coverage tells from a gap.
    /// </summary>
    public static bool TradesAroundTheClock(this MarketCategory category)
    {
        return category != MarketCategory.Moex;
    }
}