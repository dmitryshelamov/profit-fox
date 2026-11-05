using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.IdeaTrades;

/// <summary>
///     The size of a trade by an idea: the volume that loses the asked risk at the stop after the fees of <see cref="IdeaMoney" />,
///     cut to the largest amount of a trade and rounded down to the step of the instrument. The plan carries the price the trade
///     enters at, which is not always the price of the idea.
/// </summary>
public static class IdeaTradeSizing
{
    /// <param name="plan">The levels of the idea with the entry at the price the trade enters at.</param>
    /// <param name="fees"></param>
    /// <param name="risk">The most to lose at the stop in the quote asset.</param>
    /// <param name="maxAmount">The largest amount of a trade in the quote asset.</param>
    /// <param name="rules"></param>
    public static IdeaTradeSize Calculate(IdeaPlan plan, FeeRates fees, decimal risk, decimal maxAmount, InstrumentRules rules)
    {
        IdeaShares shares = IdeaMoney.PerUsdt(plan, fees);
        decimal byRisk = risk / (shares.LossPerUsdt * plan.Entry);
        decimal byAmount = maxAmount / plan.Entry;
        decimal quantity = Math.Floor(Math.Min(byRisk, byAmount) / rules.QuantityStep) * rules.QuantityStep;
        decimal amount = quantity * plan.Entry;

        return new IdeaTradeSize(quantity, amount, amount * shares.LossPerUsdt, shares.ProfitPerUsdt / shares.LossPerUsdt, byAmount < byRisk);
    }
}