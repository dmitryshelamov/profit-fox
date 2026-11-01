using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>Fee rates as fractions (<c>0.001</c> is 0.1 %) and the category that decides the asset a fee is charged in.</summary>
public sealed record FeeSchedule(decimal LimitRate, decimal MarketRate, MarketCategory Category)
{
    /// <summary>As on Bybit: a spot buy pays in the coin it buys; a spot sell and every derivative trade pay in the quote asset.</summary>
    public FeeAsset AssetOf(OrderSide side)
    {
        return Category == MarketCategory.Spot && side == OrderSide.Buy ? FeeAsset.Base : FeeAsset.Quote;
    }
}