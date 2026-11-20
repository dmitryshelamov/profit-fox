using ProfitFox.Bench.Domain.Ledger;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     A line of the ledger with the report written with it; a run that failed or was cancelled has no report.
/// </summary>
public sealed record LedgerResult(LedgerEntry Entry, string? Report);
