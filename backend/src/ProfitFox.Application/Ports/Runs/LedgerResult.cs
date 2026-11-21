using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Application.Ports.Runs;

/// <summary>
///     A line of the ledger with the report written with it; a run that failed or was cancelled has no report.
/// </summary>
public sealed record LedgerResult(LedgerEntry Entry, string? Report);
