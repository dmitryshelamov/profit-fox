namespace ProfitFox.Application.Signals;

/// <summary>
///     Why the library refused a request.
/// </summary>
public enum SignalProblem
{
    /// <summary>
    ///     The name is not allowed or the graph has errors; nothing was written.
    /// </summary>
    Invalid,

    NotFound,

    NameTaken,

    /// <summary>
    ///     The file is there but is not a signal.
    /// </summary>
    Unreadable
}
