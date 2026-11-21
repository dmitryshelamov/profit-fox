using System.Globalization;
using ProfitFox.Domain.Trades;
using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;

namespace ProfitFox.Domain.Positions;

/// <summary>
///     The trade plan of an entry: its side with the time, the stop and the target the exit nodes of the strategy give at
///     its candle (spec strategy-position «Недопустимая заявка»).
/// </summary>
public static class EntryPlan
{
    /// <summary>
    ///     The plan, or null — the refusal <c>plan</c> — when the time is missing, shorter than a minute or not whole
    ///     minutes, or when a stop or a target of the strategy is missing or not strictly between 0 and 1.
    /// </summary>
    public static TradePlan? Of(StrategyStep step, EntrySide side)
    {
        if (step.After is not { } after || Missing(step.Stop) || Missing(step.Target))
        {
            return null;
        }

        return TradePlan.OrNull(side == EntrySide.Long ? TradeSide.Long : TradeSide.Short, after, Fraction(step.Stop), Fraction(step.Target));
    }

    private static bool Missing(ExitDistance distance)
    {
        return distance is { InStrategy: true, Value: null };
    }

    // A graph counts in decimal, a plan in double. The fraction goes through its decimal text, so it becomes the double
    // the same text gives in the plan of a hypothesis (change add-strategy-graph, design.md, decision 3): the direct
    // conversion differs in the last bit for fractions of many digits, 0.498964597192726048 for one.
    private static double? Fraction(ExitDistance distance)
    {
        return distance.Value is { } value
            ? double.Parse(value.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;
    }
}
