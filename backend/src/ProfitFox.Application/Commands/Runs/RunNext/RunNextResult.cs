namespace ProfitFox.Application.Commands.Runs.RunNext;

/// <summary>
///     What the Worker found in the queue.
/// </summary>
public enum RunNextResult
{
    /// <summary>
    ///     A run was taken and gone, whatever its end.
    /// </summary>
    Ran,

    /// <summary>
    ///     The queue had nothing to take.
    /// </summary>
    NothingToTake
}
