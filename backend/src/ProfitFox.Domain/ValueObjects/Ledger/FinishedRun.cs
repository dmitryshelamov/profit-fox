namespace ProfitFox.Domain.ValueObjects.Ledger;

/// <summary>
///     A line of the ledger with the kind of its run: <see cref="Parity" /> is true for a parity run.
/// </summary>
public sealed record FinishedRun(LedgerEntry Entry, bool Parity);
