using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.IdeaTrades;

/// <summary>
///     The size of a trade by an idea: the volume the asked amount buys at the entry, rounded down to the step of the instrument, and what
///     that volume loses at the stop and earns at the target after the fees of <see cref="IdeaMoney" />. The plan carries the price the trade
///     enters at, which is not always the price of the idea.
/// </summary>
public static class IdeaTradeSizing
{
    /// <param name="plan">The levels of the idea with the entry at the price the trade enters at.</param>
    /// <param name="fees"></param>
    /// <param name="amount">The amount to trade in the quote asset.</param>
    /// <param name="rules"></param>
    public static IdeaTradeSize Calculate(IdeaPlan plan, FeeRates fees, decimal amount, InstrumentRules rules)
    {
        IdeaShares shares = IdeaMoney.PerUsdt(plan, fees);
        decimal quantity = Math.Floor(amount / plan.Entry / rules.QuantityStep) * rules.QuantityStep;
        decimal real = quantity * plan.Entry;

        return new IdeaTradeSize(quantity, real, real * shares.LossPerUsdt, real * shares.ProfitPerUsdt, shares.ProfitPerUsdt / shares.LossPerUsdt);
    }
}