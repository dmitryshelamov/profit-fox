using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The result of a closed idea in R: (exit − entry) / (entry − stop) for long, mirrored for short, less the fees in R. The entry pays the
///     taker fee at the market and with a stop order, the maker fee with a limit, every exit the taker fee. Every fill but the limit entry of a
///     pullback, which rests on the book, is worse by the slippage, and the fees are paid on the filled prices; the risk stays the distance of
///     the plan. An idea that never entered has no result, nor has one that entered and closed with no price to exit at.
/// </summary>
public static class CaseResult
{
    public static decimal? R(IdeaTrack track, FeeRates fees)
    {
        if (track.EnteredAt is null || track.ExitPrice is null)
        {
            return null;
        }

        IdeaPlan plan = track.Plan;
        decimal entry = EntryFill(plan, fees);
        decimal exit = ExitFill(plan, track.ExitPrice.Value, fees);
        decimal risk = Math.Abs(plan.Entry - plan.Stop);
        decimal paid = ((EntryFeePercent(plan, fees) * entry) + (fees.MarketPercent * exit)) / 100m;

        return ((Side(plan) * (exit - entry)) - paid) / risk;
    }

    /// <summary>The price the entry fills at: a pullback at its limit, any other entry worse than the plan by the slippage.</summary>
    public static decimal EntryFill(IdeaPlan plan, FeeRates fees)
    {
        return plan.Type == IdeaType.Pullback ? plan.Entry : plan.Entry * (1m + Slippage(plan, fees));
    }

    /// <summary>The price an exit at <paramref name="price" /> fills at, worse by the slippage: every exit goes to the market.</summary>
    public static decimal ExitFill(IdeaPlan plan, decimal price, FeeRates fees)
    {
        return price * (1m - Slippage(plan, fees));
    }

    /// <summary>The fee of the entry in percent: the maker fee of the limit of a pullback, the taker fee of any other.</summary>
    public static decimal EntryFeePercent(IdeaPlan plan, FeeRates fees)
    {
        return plan.Type == IdeaType.Pullback ? fees.LimitPercent : fees.MarketPercent;
    }

    /// <summary>+1 for long, −1 for short.</summary>
    public static decimal Side(IdeaPlan plan)
    {
        return plan.Side == IdeaSide.Long ? 1m : -1m;
    }

    // A buy fills higher and a sell lower by the slippage: the long buys its entry and sells its exit, the short the other way round.
    private static decimal Slippage(IdeaPlan plan, FeeRates fees)
    {
        return Side(plan) * fees.SlippagePercent / 100m;
    }
}