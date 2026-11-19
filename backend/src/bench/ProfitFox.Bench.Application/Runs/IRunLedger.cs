using ProfitFox.Bench.Domain.Ledger;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     The ledger of runs (spec hypothesis-bench): its lines in the order they were written, only ever appended. Its rules
///     are <see cref="LedgerRules" />.
/// </summary>
public interface IRunLedger
{
    IReadOnlyList<LedgerEntry> Entries();

    void Append(LedgerEntry entry);
}
