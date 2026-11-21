namespace ProfitFox.Domain.Verdicts;

/// <summary>
///     The verdict on a hypothesis (spec hypothesis-bench).
/// </summary>
public enum VerdictKind
{
    Passed,
    Rejected,

    /// <summary>
    ///     Too few blocks or too little power: the outcomes of the events were not read, the run may be repeated.
    /// </summary>
    InsufficientData
}
