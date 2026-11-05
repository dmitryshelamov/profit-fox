using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The money of an idea on 1 USDT of the amount of a deal, by the fee rule of <see cref="CaseResult" />: the entry pays the taker fee at the
///     market and the maker fee with a limit, the exit the taker fee. The profit goes to the target, the loss to the stop.
/// </summary>
public static class IdeaMoney
{
    public static IdeaShares PerUsdt(IdeaPlan plan, FeeRates fees)
    {
        decimal entryFee = (plan.Type == IdeaType.Pullback ? fees.LimitPercent : fees.MarketPercent) * plan.Entry / 100m;
        decimal profit = Math.Abs(plan.Target - plan.Entry) - entryFee - (fees.MarketPercent * plan.Target / 100m);
        decimal risk = Math.Abs(plan.Entry - plan.Stop);
        decimal loss = risk + entryFee + (fees.MarketPercent * plan.Stop / 100m);

        return new IdeaShares(profit / plan.Entry, loss / plan.Entry, risk / plan.Entry);
    }
}