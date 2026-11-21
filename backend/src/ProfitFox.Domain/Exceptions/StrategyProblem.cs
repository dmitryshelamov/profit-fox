namespace ProfitFox.Domain.Exceptions;

/// <summary>
///     Why the strategy library refused a request.
/// </summary>
public enum StrategyProblem
{
    /// <summary>
    ///     The name is not allowed or the graph has errors, a reference to a signal among them; nothing was written.
    /// </summary>
    Invalid,

    NotFound,

    NameTaken,

    /// <summary>
    ///     The strategy is saved but does not read.
    /// </summary>
    Unreadable
}
