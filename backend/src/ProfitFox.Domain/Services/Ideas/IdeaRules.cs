using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The levels of an idea by the rules of its type, long; short mirrors them. The stop buffer is k × ATR, at least three spreads. The levels
///     go on the price step of the pair: the stop away from the entry and the target towards it, the limit of a pullback towards the market,
///     the stop order of a stop entry away from it, so none of them is better than the rules give. An idea without the zone it needs, with a
///     reward under 1.5 risks, short on spot, or a pullback or a stop entry whose entry the price has passed is not made. A stop entry enters a
///     price step beyond the range of its setup.
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
        // The zone a breakout broke or a false breakout swept is behind the idea, though the price may still stand in it.
        Zone? behind = context.SetupZone
                       ?? (side == IdeaSide.Long ? ZoneFinder.Support(context.Zones, context.Price) : ZoneFinder.Resistance(context.Zones, context.Price));
        Zone? next = side == IdeaSide.Long ? ZoneFinder.Resistance(context.Zones, context.Price) : ZoneFinder.Support(context.Zones, context.Price);
        decimal extreme = side == IdeaSide.Long ? context.WindowLow : context.WindowHigh;

        decimal tick = context.Tick;
        decimal Stop(decimal price) => PriceStep.Round(price - (ahead * buffer), tick, ahead > 0);

        IdeaPlan? plan = type switch
        {
            IdeaType.Levels when behind is not null && next is not null => new IdeaPlan(
                type,
                side,
                context.Price,
                Stop(Edge(behind, ahead)),
                PriceStep.Round(Edge(next, ahead), tick, ahead > 0)),
            IdeaType.TwoR when behind is not null => TwoR(type, side, context.Price, Stop(Edge(behind, ahead)), tick),
            IdeaType.BeyondExtreme => TwoR(type, side, context.Price, Stop(extreme), tick),
            IdeaType.Pullback => TwoR(type, side, PriceStep.Round((context.WindowLow + context.WindowHigh) / 2, tick, ahead < 0), Stop(extreme), tick),
            IdeaType.StopEntry => TwoR(type, side, PriceStep.Round((ahead > 0 ? context.WindowHigh : context.WindowLow) + (ahead * tick), tick, ahead < 0), Stop(extreme), tick),
            _ => null
        };

        // A limit or a stop order the price has already passed would fill at once, at a price the market never gave.
        if (plan is { Type: IdeaType.Pullback } && ahead * (context.Price - plan.Entry) <= 0)
        {
            return null;
        }

        if (plan is { Type: IdeaType.StopEntry } && ahead * (plan.Entry - context.Price) <= 0)
        {
            return null;
        }

        return plan?.RiskReward >= MinimumRiskReward ? plan : null;
    }

    /// <summary>A target of 2R from the entry, on the price step towards the entry.</summary>
    public static IdeaPlan TwoR(IdeaType type, IdeaSide side, decimal entry, decimal stop, decimal tick)
    {
        return new IdeaPlan(type, side, entry, stop, PriceStep.Round(entry + (TargetRisks * (entry - stop)), tick, side == IdeaSide.Long));
    }

    // A long stop goes below the low of a support and a long target stops at the low of a resistance; short, the highs.
    private static decimal Edge(Zone zone, decimal ahead)
    {
        return ahead > 0 ? zone.Low : zone.High;
    }
}