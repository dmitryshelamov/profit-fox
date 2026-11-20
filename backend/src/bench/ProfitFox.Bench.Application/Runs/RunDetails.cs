using ProfitFox.Bench.Domain.Ledger;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     A run as the page shows it: its row in the queue, and once it has ended its line in the ledger with the report.
/// </summary>
public sealed record RunDetails(RunRecord Run, LedgerEntry? Entry, string? Report);
