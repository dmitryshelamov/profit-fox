namespace ProfitFox.Domain.Entities.Runs.Enums;

/// <summary>
///     What cancelling a run did (spec bench-run-queue «Отмена прогона»).
/// </summary>
public enum CancelOutcome
{
    /// <summary>
    ///     The backtest was waiting and is gone from the queue, with no trace.
    /// </summary>
    Removed,

    /// <summary>
    ///     The backtest is going; its Worker stops it at the next renewal and it ends <c>cancelled</c>.
    /// </summary>
    Requested,

    /// <summary>
    ///     The run has ended already.
    /// </summary>
    AlreadyFinished,

    /// <summary>
    ///     There is no such run.
    /// </summary>
    NotFound
}
