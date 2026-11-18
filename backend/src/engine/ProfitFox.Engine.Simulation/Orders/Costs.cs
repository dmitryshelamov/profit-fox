namespace ProfitFox.Engine.Simulation.Orders;

/// <summary>
///     The costs of trading, fractions and not percents: the taker fee on the value of each fill and the slippage of each market fill.
///     The caller gives them; the simulator has no defaults.
/// </summary>
public sealed record Costs
{
    /// <summary>
    ///     The taker fee, a fraction of the value of a fill.
    /// </summary>
    public decimal TakerFee { get; }

    /// <summary>
    ///     How much worse than the open of its minute a market order fills, a fraction of that open.
    /// </summary>
    public decimal Slippage { get; }

    /// <exception cref="ArgumentOutOfRangeException">A cost is negative, or the slippage is 1 or more.</exception>
    public Costs(decimal takerFee, decimal slippage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(takerFee);
        ArgumentOutOfRangeException.ThrowIfNegative(slippage);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slippage, 1m);
        TakerFee = takerFee;
        Slippage = slippage;
    }
}