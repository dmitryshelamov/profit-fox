using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The exit rules in force: the ones applied, and the default for a type and an interval without one — 24 candles, 2 R, an entry window of 4
///     candles for a pullback and of 12 for a stop entry.
/// </summary>
public sealed class IdeaExitRules(IEnumerable<IdeaExitRule> applied)
{
    private const int LifeCandles = 24;
    private const decimal TargetR = 2m;
    private const int PullbackCandles = 4;
    private const int StopEntryCandles = 12;

    private readonly Dictionary<(IdeaType, Interval), IdeaExitRule> _applied = applied.ToDictionary(rule => (rule.Type, rule.Interval));

    public IdeaExitRule Of(IdeaType type, Interval interval)
    {
        return _applied.GetValueOrDefault((type, interval)) ?? Default(type, interval);
    }

    private static IdeaExitRule Default(IdeaType type, Interval interval)
    {
        int? window = type switch
        {
            IdeaType.Pullback => PullbackCandles,
            IdeaType.StopEntry => StopEntryCandles,
            _ => null
        };

        return new IdeaExitRule(type, interval, LifeCandles, IdeaExitRule.HasTarget(type) ? TargetR : null, window, null);
    }
}