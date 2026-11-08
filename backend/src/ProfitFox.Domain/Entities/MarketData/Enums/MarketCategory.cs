namespace ProfitFox.Domain.Entities.MarketData.Enums;

/// <summary>A market the system trades on: <see cref="Spot" /> and <see cref="Linear" /> on Bybit, <see cref="Moex" /> (the shares of TQBR) on T-Invest.</summary>
public enum MarketCategory
{
    Spot,
    Linear,
    Moex
}