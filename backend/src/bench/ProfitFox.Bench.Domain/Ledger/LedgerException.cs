namespace ProfitFox.Bench.Domain.Ledger;

/// <summary>
///     The ledger refuses a run: its rules would be broken, or the same run already waits or goes. <see cref="RunId" /> is
///     the run that stands in the way, when there is one.
/// </summary>
public sealed class LedgerException(string message, string? runId = null) : Exception(message)
{
    public string? RunId { get; } = runId;
}
