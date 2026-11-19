using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.Runs;

namespace ProfitFox.Bench.Infrastructure.Runs;

/// <summary>
///     The reports of runs as markdown files (spec hypothesis-bench «Отчёт»):
///     <c>reports/H&lt;NNNN&gt;/&lt;time&gt;-&lt;period&gt;-&lt;run&gt;.md</c> in <paramref name="benchFolder" />.
/// </summary>
public sealed class ReportFiles(string benchFolder) : IRunReports
{
    public string Write(RunDesign design, LedgerEntry entry, string text)
    {
        string folder = Path.Combine(benchFolder, "reports", design.Hypothesis.Id);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"{entry.Time.UtcDateTime:yyyyMMdd-HHmmss}-{entry.Period}-{entry.RunId[..8]}.md");
        File.WriteAllText(path, text);
        return path;
    }
}
