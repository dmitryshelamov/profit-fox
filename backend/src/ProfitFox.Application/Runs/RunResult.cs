using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;
using ProfitFox.Domain.Verdicts;

namespace ProfitFox.Application.Runs;

/// <summary>
///     What a run gave: its line in the ledger, its verdict (none for a parity run), its report, the rows of its events and
///     the number of events skipped because the position of their pair was open.
/// </summary>
public sealed record RunResult(LedgerEntry Entry, Verdict? Verdict, string Report, IReadOnlyList<EventRow> Rows, int Skipped);
