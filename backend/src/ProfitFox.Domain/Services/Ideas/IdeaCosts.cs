using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The costs of an idea in R (decision 2 of add-own-idea-numbers): the share of the amount of a deal the fees and the slippage take, c, times
///     the entry over the distance from the entry to the stop. The share is the fee of the entry (<see cref="CaseResult.EntryFeePercent" />), the
///     taker fee of the exit and the slippage of every fill at the market: the exit always, the entry of every type but «Откат».
/// </summary>
public static class IdeaCosts
{
    public static decimal R(IdeaPlan plan, FeeRates fees)
    {
        int marketFills = plan.Type == IdeaType.Pullback ? 1 : 2;
        decimal share = (CaseResult.EntryFeePercent(plan, fees) + fees.MarketPercent + (marketFills * fees.SlippagePercent)) / 100m;

        return share * plan.Entry / Math.Abs(plan.Entry - plan.Stop);
    }
}