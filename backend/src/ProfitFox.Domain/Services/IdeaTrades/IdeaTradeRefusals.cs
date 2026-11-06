using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.Services.IdeaTrades;

/// <summary>Every reason that stops an entry by an idea. It only names them: the API shows them and refuses the entry.</summary>
public static class IdeaTradeRefusals
{
    /// <returns>The reasons in a fixed order, empty when the entry may go.</returns>
    public static IReadOnlyList<IdeaTradeRefusal> Check(IdeaTradeEntry entry, IdeaTradingLimits limits, OpenIdeaTrades open, IdeaTradeAccount account)
    {
        var reasons = new List<IdeaTradeRefusal>();
        bool isLong = entry.Plan.Side == IdeaSide.Long;

        if (entry.IdeaStatus is not (IdeaStatus.AwaitingEntry or IdeaStatus.Active))
        {
            reasons.Add(IdeaTradeRefusal.IdeaNotOpen);
        }

        if (isLong ? entry.Current <= entry.Plan.Stop : entry.Current >= entry.Plan.Stop)
        {
            reasons.Add(IdeaTradeRefusal.PriceBeyondStop);
        }

        if (isLong ? entry.Current >= entry.Plan.Target : entry.Current <= entry.Plan.Target)
        {
            reasons.Add(IdeaTradeRefusal.PriceBeyondTarget);
        }

        if (entry.Size.RiskReward < limits.MinRiskReward)
        {
            reasons.Add(IdeaTradeRefusal.RiskRewardTooLow);
        }

        if (open.Count >= limits.MaxOpenTrades)
        {
            reasons.Add(IdeaTradeRefusal.TooManyOpenTrades);
        }

        if (open.Risk + entry.Size.Risk > limits.MaxTotalRisk)
        {
            reasons.Add(IdeaTradeRefusal.TotalRiskExceeded);
        }

        StrategyDirection direction = isLong ? StrategyDirection.Long : StrategyDirection.Short;

        if (entry.Category == MarketCategory.Spot)
        {
            reasons.Add(IdeaTradeRefusal.SpotNotSupported);
        }

        // A stop entry waits for the price to leave the range of its setup; the entry goes at the market or with a limit only.
        if (entry.Plan.Type == IdeaType.StopEntry)
        {
            reasons.Add(IdeaTradeRefusal.StopEntryNotSupported);
        }

        if (entry.Category == MarketCategory.Linear && account.SymbolDirections.Any(other => other != direction))
        {
            reasons.Add(IdeaTradeRefusal.OppositeSideOpen);
        }

        if (!StartFunds.Enough(account.FreeMoney, entry.Size.Amount, account.Bots, account.PendingEntries))
        {
            reasons.Add(IdeaTradeRefusal.NotEnoughMoney);
        }

        if (entry.Size.Quantity < entry.Rules.MinQuantity)
        {
            reasons.Add(IdeaTradeRefusal.QuantityBelowMinimum);
        }

        if (entry.Size.Amount < entry.Rules.MinAmount)
        {
            reasons.Add(IdeaTradeRefusal.AmountBelowMinimum);
        }

        switch (account.Access)
        {
            case AccountAccess.NoKey:
                reasons.Add(IdeaTradeRefusal.NoKey);

                break;
            case AccountAccess.CategoryNotTraded:
                reasons.Add(IdeaTradeRefusal.CategoryNotTraded);

                break;
            case AccountAccess.HedgeMode:
                reasons.Add(IdeaTradeRefusal.HedgeMode);

                break;
        }

        return reasons;
    }
}