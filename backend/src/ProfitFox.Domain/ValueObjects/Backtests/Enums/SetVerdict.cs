namespace ProfitFox.Domain.ValueObjects.Backtests.Enums;

/// <summary>
///     The mark of a set of a category of ideas (spec strategy-backtest «Отметка набора»).
/// </summary>
public enum SetVerdict
{
    /// <summary>
    ///     «кандидат»: a set of a backtest without a source met all five conditions; it is worth a run on the rest of history.
    /// </summary>
    Candidate,

    /// <summary>
    ///     «прошёл»: a set of a backtest on the rest of history met all five conditions.
    /// </summary>
    Passed,

    /// <summary>
    ///     «не прошёл»: on the rest of history the mean of the weekly means or the difference is there and not above zero.
    /// </summary>
    Failed,

    /// <summary>
    ///     «не доказан»: on the rest of history neither passed nor failed.
    /// </summary>
    Unproven
}