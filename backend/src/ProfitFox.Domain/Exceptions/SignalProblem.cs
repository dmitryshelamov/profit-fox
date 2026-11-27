namespace ProfitFox.Domain.Exceptions;

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
    Unreadable,

    /// <summary>
    ///     A version of a saved strategy points at the signal, so it is not deleted (spec signal-constructor «Сигнал, на
    ///     который ссылается стратегия»).
    /// </summary>
    InUse
}
