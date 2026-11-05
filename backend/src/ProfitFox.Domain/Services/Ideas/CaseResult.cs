using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The result of a closed idea in R: (exit − entry) / (entry − stop) for long, mirrored for short, less the fees in R. The entry pays the
///     taker fee at the market and the maker fee with a limit, every exit the taker fee. An idea that never entered has no result.
/// </summary>
public static class CaseResult
{
    public static decimal? R(IdeaTrack track, FeeRates fees)
    {
        if (track.EnteredAt is null)
        {
            return null;
        }

        IdeaPlan plan = track.Plan;
        decimal exit = track.ExitPrice!.Value;
        decimal risk = Math.Abs(plan.Entry - plan.Stop);
        decimal gain = plan.Side == IdeaSide.Long ? exit - plan.Entry : plan.Entry - exit;
        decimal entryFee = plan.Type == IdeaType.Pullback ? fees.LimitPercent : fees.MarketPercent;
        decimal paid = ((entryFee * plan.Entry) + (fees.MarketPercent * exit)) / 100m;

        return (gain - paid) / risk;
    }
}