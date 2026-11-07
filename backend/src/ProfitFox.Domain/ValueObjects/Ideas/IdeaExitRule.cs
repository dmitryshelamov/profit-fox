using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The exit rule of a type of idea on an interval (decision 5): the lifetime in candles from the entry, the target in R of the types that
///     have one and the entry window in candles of a pullback and a stop entry. A rule never applied has no <see cref="AppliedAt" />.
/// </summary>
public sealed record IdeaExitRule(IdeaType Type, Interval Interval, int LifeCandles, decimal? TargetR, int? EntryCandles, DateTimeOffset? AppliedAt)
{
    private const decimal TargetStep = 0.5m;
    private const decimal LowestTarget = 1m;
    private const decimal HighestTarget = 5m;
    private static readonly TimeSpan ShortestHolding = TimeSpan.FromHours(1);

    /// <summary>
    ///     How long the outcome of an idea of the rule may last from its firing (decision 1 of cluster-idea-cases-by-holding): its entry window
    ///     and lifetime in candles of the interval, never shorter than an hour, as one move of the market lasts.
    /// </summary>
    public TimeSpan Holding
    {
        get
        {
            TimeSpan holding = (LifeCandles + (EntryCandles ?? 0)) * (Interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch);

            return holding < ShortestHolding ? ShortestHolding : holding;
        }
    }

    /// <summary>Whether the target of the type is in R; the one of «Уровни» is the next zone.</summary>
    public static bool HasTarget(IdeaType type)
    {
        return type != IdeaType.Levels;
    }

    /// <summary>Whether the type waits for its entry.</summary>
    public static bool HasWindow(IdeaType type)
    {
        return type is IdeaType.Pullback or IdeaType.StopEntry;
    }

    /// <summary>
    ///     The names of the values a rule cannot take, <c>life</c>, <c>target</c> and <c>window</c>: off the grid of the interval, a target off
    ///     the steps of 0.5 R from 1 to 5 R, a value the type has no use for or one it needs and lacks.
    /// </summary>
    public static IReadOnlyList<string> Invalid(IdeaType type, Interval interval, int life, decimal? target, int? window)
    {
        IReadOnlyList<int> grid = ExitRuleGrid.Of(interval);
        var invalid = new List<string>();

        if (!grid.Contains(life))
        {
            invalid.Add("life");
        }

        if (HasTarget(type) ? target is not { } r || r < LowestTarget || r > HighestTarget || r % TargetStep != 0 : target is not null)
        {
            invalid.Add("target");
        }

        if (HasWindow(type) ? window is not { } candles || !grid.Contains(candles) : window is not null)
        {
            invalid.Add("window");
        }

        return invalid;
    }
}