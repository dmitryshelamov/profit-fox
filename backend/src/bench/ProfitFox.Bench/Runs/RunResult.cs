using ProfitFox.Bench.Ledger;
using ProfitFox.Bench.Verdicts;

namespace ProfitFox.Bench.Runs;

/// <summary>
///     What a run gave: its line in the ledger, its verdict (none for a parity run) and its report.
/// </summary>
public sealed record RunResult(LedgerEntry Entry, Verdict? Verdict, string ReportPath, IReadOnlyList<EventRow> Rows);
