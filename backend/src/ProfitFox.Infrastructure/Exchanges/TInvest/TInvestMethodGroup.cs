namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>A group of methods of Invest API: the limits of a token are counted per group, a minute each.</summary>
internal enum TInvestMethodGroup
{
    Users,
    Instruments,
    MarketData,
    Operations,
    Orders
}