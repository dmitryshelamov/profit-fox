using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The money of an idea on 1 USDT of the amount of a deal, by the fee and slippage rule of <see cref="CaseResult" />: the entry pays the
///     taker fee at the market and the maker fee with a limit, the exit the taker fee, every fill but a limit entry is worse by the slippage.
///     The profit goes to the target, the loss to the stop.
/// </summary>
public static class IdeaMoney
{
    public static IdeaShares PerUsdt(IdeaPlan plan, FeeRates fees)
    {
        decimal side = CaseResult.Side(plan);
        decimal entry = CaseResult.EntryFill(plan, fees);
        decimal target = CaseResult.ExitFill(plan, plan.Target, fees);
        decimal stop = CaseResult.ExitFill(plan, plan.Stop, fees);
        decimal entryFee = CaseResult.EntryFeePercent(plan, fees) * entry / 100m;
        decimal profit = (side * (target - entry)) - entryFee - (fees.MarketPercent * target / 100m);
        decimal risk = Math.Abs(plan.Entry - plan.Stop);
        decimal loss = (side * (entry - stop)) + entryFee + (fees.MarketPercent * stop / 100m);

        return new IdeaShares(profit / plan.Entry, loss / plan.Entry, risk / plan.Entry);
    }
}