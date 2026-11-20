using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Bench.Domain.Verdicts;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     What a run gave: its line in the ledger, its verdict (none for a parity run), its report and the rows of its events.
/// </summary>
public sealed record RunResult(LedgerEntry Entry, Verdict? Verdict, string Report, IReadOnlyList<EventRow> Rows);
