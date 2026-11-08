using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>
///     The checks of a paper entry, which depend on the idea and the amount only: the price beyond the stop or the target, the reward to risk
///     below the minimum of the auto, the amount above the largest, the volume or amount below the minimums of the instrument, rules not known yet,
///     the stop too far for the leverage of the settings on <c>linear</c>, the pair held by the auto itself, whose open trades hold the paper trades opened earlier in the minute. The budgets, the money of the account, its key, hedge mode, the opposite side and the guards are no part of it: an auto that is full does not reach the check.
/// </summary>
public static class PaperEntry
{
    /// <param name="auto"></param>
    /// <param name="idea"></param>
    /// <param name="birth">The minute the idea was born in; its close is the last price.</param>
    /// <param name="rules">The rules of the instrument, none while they are unknown.</param>
    /// <param name="fees"></param>
    /// <param name="maxAmount">«Не больше суммы на сделку» of the settings.</param>
    /// <param name="leverage">«Плечо на фьючерсах» of the settings: only the stop is checked against it, the exchange is never asked.</param>
    /// <param name="books"></param>
    public static PaperEntryCheck Check(IdeaAuto auto, Idea idea, IdeaCandle birth, InstrumentRules? rules, FeeRates fees, decimal maxAmount, int leverage, AutoBooks books)
    {
        IdeaPlan plan = idea.Track.Plan;
        var refusals = new List<AutoRefusal>();
        bool isLong = plan.Side == IdeaSide.Long;
        decimal current = birth.Close;
        decimal entry = PaperFill.EntryPrice(idea, birth);
        IdeaTradeSize? size = rules is null ? null : IdeaTradeSizing.Calculate(plan with { Entry = entry }, fees, auto.Amount, rules);

        if (isLong ? current <= plan.Stop : current >= plan.Stop)
        {
            refusals.Add(new AutoRefusal(IdeaTradeRefusal.PriceBeyondStop));
        }

        if (isLong ? current >= plan.Target : current <= plan.Target)
        {
            refusals.Add(new AutoRefusal(IdeaTradeRefusal.PriceBeyondTarget));
        }

        if (size is null)
        {
            refusals.Add(new AutoRefusal(IdeaTradeRefusal.RulesUnknown));
        }
        else if (size.RiskReward < auto.Conditions.MinRiskReward)
        {
            refusals.Add(new AutoRefusal(IdeaTradeRefusal.RiskRewardTooLow, RefusalDetail.Numbers(size.RiskReward, auto.Conditions.MinRiskReward)));
        }

        if (auto.Amount > maxAmount)
        {
            refusals.Add(new AutoRefusal(IdeaTradeRefusal.AmountAboveMax, RefusalDetail.Numbers(auto.Amount, maxAmount)));
        }

        if (rules is not null && size is not null)
        {
            if (size.Quantity < rules.MinQuantity)
            {
                refusals.Add(new AutoRefusal(IdeaTradeRefusal.QuantityBelowMinimum));
            }

            if (size.Amount < rules.MinAmount)
            {
                refusals.Add(new AutoRefusal(IdeaTradeRefusal.AmountBelowMinimum));
            }
        }

        if (idea.Instrument.Category == MarketCategory.Linear && IdeaTradeRefusals.StopTooFar(entry, plan.Stop, leverage))
        {
            refusals.Add(new AutoRefusal(IdeaTradeRefusal.StopTooFarForLeverage, IdeaTradeRefusals.StopDetail(entry, plan.Stop, leverage)));
        }

        if (AutoRefusals.OwnPair(auto, idea.Instrument, books) is { } held)
        {
            refusals.Add(held);
        }

        return new PaperEntryCheck(refusals, size);
    }
}