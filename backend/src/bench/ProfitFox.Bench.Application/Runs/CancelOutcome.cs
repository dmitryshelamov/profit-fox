namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     What cancelling a run did (spec bench-run-queue «Отмена прогона»).
/// </summary>
public enum CancelOutcome
{
    /// <summary>
    ///     The run was waiting and is gone from the queue, with no trace in the ledger.
    /// </summary>
    Removed,

    /// <summary>
    ///     The run is going; its Worker stops it at the next renewal and the ledger gets a <c>cancelled</c> line.
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
