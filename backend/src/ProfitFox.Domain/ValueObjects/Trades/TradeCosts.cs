namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The costs of a trade as fractions of its amount: the taker fee per side, the slippage per market fill, whether
///     funding payments during the hold count, and the maker fee that a fill of a limit order pays (change
///     add-position-node, design.md, decision 8). The defaults are the base rates of Bybit for perpetuals. A trade of a
///     plan pays the taker fee on both sides, the target too.
/// </summary>
public sealed record TradeCosts(double TakerFee = 0.00055, double Slippage = 0.0005, bool Funding = true, double MakerFee = 0.0002)
{
    public static readonly TradeCosts Default = new TradeCosts();
}
