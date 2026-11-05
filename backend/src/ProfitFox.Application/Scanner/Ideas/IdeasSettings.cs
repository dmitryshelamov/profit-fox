using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The <c>Ideas</c> section of the scanner's settings (decision 7): the thresholds of the setups, common to all pairs, with a value of
///     their own on an interval if set; k of the stop buffer and the rules of the zones. The live ideas and the replay of history run by the
///     same rules. The starting thresholds are provisional, replays of new versions tune them. The API reads the same section for its own
///     idea and the zones.
/// </summary>
public sealed class IdeasSettings
{
    /// <summary>The intervals of the setups: 5 and 15 minutes, 1 and 4 hours.</summary>
    public static readonly IReadOnlyList<Interval> Intervals = [Interval.FiveMinutes, Interval.FifteenMinutes, Interval.OneHour, Interval.FourHours];

    public ThresholdSettings Thresholds { get; init; } = new ThresholdSettings();

    /// <summary>Thresholds of their own by interval code (<c>5</c>, <c>15</c>, <c>60</c>, <c>240</c>); a value not set is the common one.</summary>
    public Dictionary<string, ThresholdOverride> ByInterval { get; init; } = [];

    /// <summary>k: the stop buffer is k × ATR(14) of the idea's interval, at least three spreads.</summary>
    public decimal StopBuffer { get; init; } = 1.5m;

    /// <summary>Touches within this many ATR of the lowest one of a zone join it.</summary>
    public decimal ZoneMerge { get; init; } = 0.5m;

    /// <summary>The touches a zone needs.</summary>
    public int ZoneStrength { get; init; } = 2;

    /// <summary>The rules of the ideas, live and in the replay of history alike.</summary>
    public IdeaRuleSet Rules()
    {
        return new IdeaRuleSet(
            Intervals.ToDictionary(interval => interval, interval => Thresholds.With(ByInterval.GetValueOrDefault(interval.ToCode()))),
            StopBuffer,
            new ZoneRules(ZoneMerge, ZoneStrength));
    }
}