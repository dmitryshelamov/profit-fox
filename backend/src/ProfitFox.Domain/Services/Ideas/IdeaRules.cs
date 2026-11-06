using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The levels of an idea by the rules of its type, long; short mirrors them. The stop buffer is k × ATR, at least three spreads. The levels
///     go on the price step of the pair: the stop away from the entry and the target towards it, the limit of a pullback towards the market,
///     the stop order of a stop entry away from it, so none of them is better than the rules give. A target in R is c risks from the entry, c the
///     target of the exit rule of the type × interval. An idea without the zone it needs, «Уровни» with a reward under 1.5 risks, a stop or a
///     target not above zero, short on spot, or a pullback or a stop entry whose entry the price has passed is not made. A stop entry enters a
///     price step beyond the range of its setup.
/// </summary>
public static class IdeaRules
{
    private const decimal MinimumRiskReward = 1.5m;
    private const int SpreadsInBuffer = 3;

    /// <param name="type">The type of the idea.</param>
    /// <param name="side">The side of the idea.</param>
    /// <param name="context">What the rules take on the minute the idea is made.</param>
    /// <param name="targetR">c of a type with a target in R; none for «Уровни».</param>
    public static IdeaPlan? Plan(IdeaType type, IdeaSide side, IdeaContext context, decimal? targetR)
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
            IdeaType.TwoR when behind is not null => InR(type, side, context.Price, Stop(Edge(behind, ahead)), targetR!.Value, tick),
            IdeaType.BeyondExtreme => InR(type, side, context.Price, Stop(extreme), targetR!.Value, tick),
            IdeaType.Pullback => InR(type, side, PriceStep.Round((context.WindowLow + context.WindowHigh) / 2, tick, ahead < 0), Stop(extreme), targetR!.Value, tick),
            IdeaType.StopEntry => InR(type, side, PriceStep.Round((ahead > 0 ? context.WindowHigh : context.WindowLow) + (ahead * tick), tick, ahead < 0), Stop(extreme), targetR!.Value, tick),
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

        // A price is above zero: a short target or a long stop far enough off the entry to cross it is no level an order can take.
        if (plan is { Target: <= 0 } or { Stop: <= 0 })
        {
            return null;
        }

        return plan is { Type: IdeaType.Levels, RiskReward: < MinimumRiskReward } ? null : plan;
    }

    /// <summary>A target of <paramref name="targetR" /> risks from the entry, on the price step towards the entry.</summary>
    public static IdeaPlan InR(IdeaType type, IdeaSide side, decimal entry, decimal stop, decimal targetR, decimal tick)
    {
        return new IdeaPlan(type, side, entry, stop, PriceStep.Round(entry + (targetR * (entry - stop)), tick, side == IdeaSide.Long));
    }

    // A long stop goes below the low of a support and a long target stops at the low of a resistance; short, the highs.
    private static decimal Edge(Zone zone, decimal ahead)
    {
        return ahead > 0 ? zone.Low : zone.High;
    }
}