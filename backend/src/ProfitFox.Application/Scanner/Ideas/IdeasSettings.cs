using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The <c>Ideas</c> section of the scanner's settings (decision 7): the thresholds of the setups, common to all pairs, with a value of
///     their own on an interval if set; the floors the replay of history writes cases with; k of the stop buffer and the rules of the zones. The
///     starting thresholds are provisional, the replay tunes them. The API reads the same section for its own idea and the zones.
/// </summary>
public sealed class IdeasSettings
{
    /// <summary>The intervals of the setups: 5 and 15 minutes, 1 and 4 hours.</summary>
    public static readonly IReadOnlyList<Interval> Intervals = [Interval.FiveMinutes, Interval.FifteenMinutes, Interval.OneHour, Interval.FourHours];

    public ThresholdSettings Thresholds { get; init; } = new ThresholdSettings();

    /// <summary>Thresholds of their own by interval code (<c>5</c>, <c>15</c>, <c>60</c>, <c>240</c>); a value not set is the common one.</summary>
    public Dictionary<string, ThresholdOverride> ByInterval { get; init; } = [];

    /// <summary>The floors of the replay of history; the number of candles is the threshold's own.</summary>
    public ThresholdSettings Floors { get; init; } = new ThresholdSettings { Move = 2m, Volume = 1.5m, EarlyMove = 1m, EarlyVolume = 2m };

    /// <summary>k: the stop buffer is k × ATR(14) of the idea's interval, at least three spreads.</summary>
    public decimal StopBuffer { get; init; } = 1.5m;

    /// <summary>Touches within this many ATR of the lowest one of a zone join it.</summary>
    public decimal ZoneMerge { get; init; } = 0.5m;

    /// <summary>The touches a zone needs.</summary>
    public int ZoneStrength { get; init; } = 2;

    /// <summary>The rules of live ideas: the thresholds.</summary>
    public IdeaRuleSet Live()
    {
        return Rules(Of);
    }

    /// <summary>The rules of the replay of history: the floors with the number of candles of the thresholds.</summary>
    public IdeaRuleSet Replay()
    {
        return Rules(interval => Floors.With(null) with { Candles = Of(interval).Candles });
    }

    private SetupThresholds Of(Interval interval)
    {
        return Thresholds.With(ByInterval.GetValueOrDefault(interval.ToCode()));
    }

    private IdeaRuleSet Rules(Func<Interval, SetupThresholds> thresholds)
    {
        return new IdeaRuleSet(Intervals.ToDictionary(interval => interval, thresholds), StopBuffer, new ZoneRules(ZoneMerge, ZoneStrength));
    }
}