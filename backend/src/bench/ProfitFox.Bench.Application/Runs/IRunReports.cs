using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.Runs;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     Where the reports of runs are kept (spec hypothesis-bench «Отчёт»).
/// </summary>
public interface IRunReports
{
    /// <summary>
    ///     Keeps the <paramref name="text" /> of the report of a run and tells where it is.
    /// </summary>
    string Write(RunDesign design, LedgerEntry entry, string text);
}
