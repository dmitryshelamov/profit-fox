namespace ProfitFox.Domain.ValueObjects.Strategies.Enums;

/// <summary>
///     The kind of a strategy (spec strategy-graph «Вид стратегии»), chosen by hand when it is created and never changed:
///     a strategy for a bot is any graph that passes the checks of a strategy; a category of ideas is a strategy by plan —
///     entries by events with a time, a stop and a target when wanted.
/// </summary>
public enum StrategyKind
{
    Bot,
    Idea
}
