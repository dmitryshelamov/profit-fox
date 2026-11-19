namespace ProfitFox.Bench.Domain.Ledger;

/// <summary>
///     The ledger refuses a run: its rules would be broken.
/// </summary>
public sealed class LedgerException(string message) : Exception(message);
