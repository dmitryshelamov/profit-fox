namespace ProfitFox.Domain.Trades;

/// <summary>
///     The costs of a trade as fractions of its amount: the taker fee per side, the slippage per market fill, and whether
///     funding payments during the hold count.
/// </summary>
public sealed record TradeCosts(double TakerFee = 0.00055, double Slippage = 0.0005, bool Funding = true)
{
    public static readonly TradeCosts Default = new TradeCosts();
}
