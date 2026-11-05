using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The levels of an idea by the rules of its type, long; short mirrors them. The stop buffer is k × ATR, at least three spreads. An idea
///     without the zone it needs, with a reward under 1.5 risks, short on spot, or a pullback whose limit the price has passed is not made.
/// </summary>
public static class IdeaRules
{
    private const decimal MinimumRiskReward = 1.5m;
    private const decimal TargetRisks = 2m;
    private const int SpreadsInBuffer = 3;

    public static IdeaPlan? Plan(IdeaType type, IdeaSide side, IdeaContext context)
    {
        if (side == IdeaSide.Short && context.Category == MarketCategory.Spot)
        {
            return null;
        }

        // Long ahead is +1, short −1: a stop lies behind the entry, a target ahead of it.
        decimal ahead = side == IdeaSide.Long ? 1m : -1m;
        decimal buffer = Math.Max(context.StopBuffer * context.Atr, SpreadsInBuffer * (context.Spread ?? 0m));
        Zone? behind = side == IdeaSide.Long ? ZoneFinder.Support(context.Zones, context.Price) : ZoneFinder.Resistance(context.Zones, context.Price);
        Zone? next = side == IdeaSide.Long ? ZoneFinder.Resistance(context.Zones, context.Price) : ZoneFinder.Support(context.Zones, context.Price);
        decimal extreme = side == IdeaSide.Long ? context.WindowLow : context.WindowHigh;

        IdeaPlan? plan = type switch
        {
            IdeaType.Levels when behind is not null && next is not null => new IdeaPlan(type, side, context.Price, Edge(behind, ahead) - (ahead * buffer), Edge(next, ahead)),
            IdeaType.TwoR when behind is not null => TwoR(type, side, context.Price, Edge(behind, ahead) - (ahead * buffer)),
            IdeaType.BeyondExtreme => TwoR(type, side, context.Price, extreme - (ahead * buffer)),
            IdeaType.Pullback => TwoR(type, side, (context.WindowLow + context.WindowHigh) / 2, extreme - (ahead * buffer)),
            _ => null
        };

        // A limit the price has already passed would fill at once, at a price the market never gave.
        if (plan is { Type: IdeaType.Pullback } && ahead * (context.Price - plan.Entry) <= 0)
        {
            return null;
        }

        return plan?.RiskReward >= MinimumRiskReward ? plan : null;
    }

    private static IdeaPlan TwoR(IdeaType type, IdeaSide side, decimal entry, decimal stop)
    {
        return new IdeaPlan(type, side, entry, stop, entry + (TargetRisks * (entry - stop)));
    }

    // A long stop goes below the low of a support and a long target stops at the low of a resistance; short, the highs.
    private static decimal Edge(Zone zone, decimal ahead)
    {
        return ahead > 0 ? zone.Low : zone.High;
    }
}