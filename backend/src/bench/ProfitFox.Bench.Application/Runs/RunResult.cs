using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Bench.Domain.Verdicts;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     What a run gave: its line in the ledger, its verdict (none for a parity run) and its report.
/// </summary>
public sealed record RunResult(LedgerEntry Entry, Verdict? Verdict, string ReportPath, IReadOnlyList<EventRow> Rows);
