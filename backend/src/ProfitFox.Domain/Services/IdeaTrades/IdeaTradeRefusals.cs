using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
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

        if (entry.Asked > limits.MaxAmount)
        {
            reasons.Add(IdeaTradeRefusal.AmountAboveMax);
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

        // A stop entry waits for the price to leave the range of its setup; the entry goes at the market or with a limit only.
        if (entry.Plan.Type == IdeaType.StopEntry)
        {
            reasons.Add(IdeaTradeRefusal.StopEntryNotSupported);
        }

        if (entry.Category == MarketCategory.Linear && StopTooFar(entry.Price, entry.Plan.Stop, entry.Leverage))
        {
            reasons.Add(IdeaTradeRefusal.StopTooFarForLeverage);
        }

        if (entry.Category == MarketCategory.Linear && account.SymbolDirections.Any(other => other != direction))
        {
            reasons.Add(IdeaTradeRefusal.OppositeSideOpen);
        }

        // The leverage of a symbol the account already holds or waits on is never changed: it is set only on an empty one. A trade by an idea that
        // waits holds the symbol though it may have sent nothing yet, and the leverage it entered with counts as much as the one the exchange shows.
        if (account.Symbol is { } symbol && symbol.IsHeld && OtherLeverage(symbol, entry.Leverage) is not null)
        {
            reasons.Add(IdeaTradeRefusal.LeverageDiffers);
        }

        if (!StartFunds.Enough(account.FreeMoney, entry.Size.Amount / entry.Leverage, account.Bots, account.PendingEntries))
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

    /// <summary>What a reason with numbers is about: the reward to risk and its lowest, the free money and the amount asked, and the like. None for a reason without.</summary>
    public static RefusalDetail? DetailOf(IdeaTradeRefusal reason, IdeaTradeEntry entry, IdeaTradingLimits limits, OpenIdeaTrades open, IdeaTradeAccount account)
    {
        return reason switch
        {
            IdeaTradeRefusal.RiskRewardTooLow => RefusalDetail.Numbers(entry.Size.RiskReward, limits.MinRiskReward),
            IdeaTradeRefusal.NotEnoughMoney => RefusalDetail.Numbers(account.FreeMoney, entry.Asked),
            IdeaTradeRefusal.AmountAboveMax => RefusalDetail.Numbers(entry.Asked, limits.MaxAmount),
            IdeaTradeRefusal.TooManyOpenTrades => RefusalDetail.Numbers(open.Count, limits.MaxOpenTrades),
            IdeaTradeRefusal.TotalRiskExceeded => RefusalDetail.Numbers(open.Risk + entry.Size.Risk, limits.MaxTotalRisk),
            IdeaTradeRefusal.LeverageDiffers => RefusalDetail.Numbers(OtherLeverage(account.Symbol!, entry.Leverage)!.Value, entry.Leverage),
            IdeaTradeRefusal.StopTooFarForLeverage => StopDetail(entry.Price, entry.Plan.Stop, entry.Leverage),
            _ => null
        };
    }

    /// <summary>The leverage on the symbol other than that of the entry: the one the exchange shows, else the known one of a trade on it; none when all agree.</summary>
    private static decimal? OtherLeverage(LinearSymbol symbol, int leverage)
    {
        return symbol.Leverage != leverage ? symbol.Leverage : symbol.Trades.FirstOrDefault(trade => trade is { } known && known != leverage);
    }

    /// <summary>Whether the stop is at least half the way to the liquidation at the leverage: <c>|price − stop| / price ≥ 1 / (2 × leverage)</c>.</summary>
    public static bool StopTooFar(decimal price, decimal stop, int leverage)
    {
        (decimal share, decimal limit) = StopForLeverage(price, stop, leverage);

        return share >= limit;
    }

    /// <summary>
    ///     How far the stop is from the price of the entry and how far the leverage lets it be, both in percent of the price. The limit is half the
    ///     way to the liquidation, <c>1 / (2 × leverage)</c>: the rest is room for the maintenance margin, the fees and the slippage of the stop.
    /// </summary>
    private static (decimal Share, decimal Limit) StopForLeverage(decimal price, decimal stop, int leverage)
    {
        return (Math.Abs(price - stop) / price * 100m, 100m / (2m * leverage));
    }

    /// <summary>The share of the stop and its limit at the leverage, as the reason <see cref="IdeaTradeRefusal.StopTooFarForLeverage" /> tells them.</summary>
    public static RefusalDetail StopDetail(decimal price, decimal stop, int leverage)
    {
        (decimal share, decimal limit) = StopForLeverage(price, stop, leverage);

        return RefusalDetail.Numbers(share, limit);
    }
}