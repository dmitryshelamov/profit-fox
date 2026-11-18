namespace ProfitFox.Bench.Ledger;

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
    ///     A blind run for the check against the Python bench: no event outcomes, no verdict.
    /// </summary>
    public const string Parity = "parity";

    public static bool IsFinal(string status)
    {
        return status is Passed or Rejected;
    }
}
