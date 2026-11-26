using ProfitFox.Domain.ValueObjects.Backtests.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The mark of a set of a category of ideas and its five conditions (spec strategy-backtest «Отметка набора»; change
///     fix-backtest-random-control, design.md, decisions 10–12), made from the numbers of the set when they are read: it is
///     not kept. A set of a backtest without a source is «кандидат» when it met all five conditions and has no mark
///     otherwise. A set of a backtest on the rest of history — one with a source — «прошёл» with all five, «не прошёл» when
///     the mean of the weekly means or the difference is there and not above zero, and «не доказан» otherwise. The mark limits
///     nothing: no backtest is refused for it, and it does not change which strategies give ideas.
/// </summary>
public sealed record SetMark(SetVerdict? Verdict, MarkConditions Conditions)
{
    /// <summary>
    ///     The first engine version whose sets have a mark: before it the difference was counted the old way and the numbers
    ///     by weeks were not kept.
    /// </summary>
    public const int FirstEngineVersion = 5;

    /// <summary>
    ///     The mark of a set; none for a strategy for a bot and for a backtest of an engine version before
    ///     <see cref="FirstEngineVersion" />.
    /// </summary>
    /// <param name="numbers">The numbers of the set over every pair.</param>
    /// <param name="kind">The kind of the strategy.</param>
    /// <param name="engineVersion">The engine version of the backtest.</param>
    /// <param name="onRest">The backtest is on the rest of history: it has a source.</param>
    public static SetMark? Of(SetSummary numbers, StrategyKind kind, int? engineVersion, bool onRest)
    {
        if (kind != StrategyKind.Idea || engineVersion is not >= FirstEngineVersion)
        {
            return null;
        }

        var conditions = MarkConditions.Of(numbers);
        if (!onRest)
        {
            return new SetMark(conditions.All ? SetVerdict.Candidate : null, conditions);
        }

        if (conditions.All)
        {
            return new SetMark(SetVerdict.Passed, conditions);
        }

        bool failed = numbers.Weekly is { Mean: <= 0 } || numbers.Difference is { Mean: <= 0 };
        return new SetMark(failed ? SetVerdict.Failed : SetVerdict.Unproven, conditions);
    }
}