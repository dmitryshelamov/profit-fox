namespace ProfitFox.Domain.ValueObjects.Ledger;

/// <summary>
///     The status of a run in the ledger.
/// </summary>
public static class RunStatus
{
    public const string Passed = "passed";
    public const string Rejected = "rejected";
    public const string InsufficientData = "insufficient_data";

    /// <summary>
    ///     The run stopped on an error before its verdict.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    ///     A run that was started and then cancelled; it is not a verdict and not final.
    /// </summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    ///     A blind run for the check against the Python bench: no event outcomes, no verdict.
    /// </summary>
    public const string Parity = "parity";

    public static bool IsFinal(string status)
    {
        return status is Passed or Rejected;
    }
}
