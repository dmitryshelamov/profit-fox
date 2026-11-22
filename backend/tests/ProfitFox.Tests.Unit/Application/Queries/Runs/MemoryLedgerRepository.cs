using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Tests.Unit.Application.Queries.Runs;

/// <summary>
///     The ledger in memory, for the queries that only read it: the lines a test wrote, in the order it did. The decisions
///     are the job of the database repository.
/// </summary>
public sealed class MemoryLedgerRepository : ILedgerRepository
{
    private readonly List<FinishedRun> _lines = [];

    /// <summary>
    ///     How many times the last lines of versions were read.
    /// </summary>
    public int LastReads { get; private set; }

    public void Add(LedgerEntry entry, bool parity = false)
    {
        _lines.Add(new FinishedRun(entry, parity));
    }

    public Task<IReadOnlyList<LedgerEntry>> EntriesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<LedgerEntry>>([.. _lines.Select(line => line.Entry)]);
    }

    public Task<IReadOnlyList<FinishedRun>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<FinishedRun>>([.. Enumerable.Reverse(_lines).Take(limit)]);
    }

    public Task<IReadOnlyList<FinishedRun>> LastAsync(IReadOnlyCollection<HypothesisVersion> versions, CancellationToken cancellationToken)
    {
        LastReads++;
        FinishedRun[] last =
        [
            .. versions
                .Distinct()
                .Select(version => _lines
                    .Select((line, index) => (Line: line, Index: index))
                    .Where(item => item.Line.Entry.Hypothesis == version.Id && item.Line.Entry.Version == version.Version)
                    .OrderByDescending(item => item.Line.Entry.Time)
                    .ThenByDescending(item => item.Index)
                    .Select(item => item.Line)
                    .FirstOrDefault())
                .OfType<FinishedRun>()
        ];
        return Task.FromResult<IReadOnlyList<FinishedRun>>(last);
    }

    public Task<LedgerResult?> FindAsync(string runId, CancellationToken cancellationToken)
    {
        FinishedRun? line = _lines.FirstOrDefault(item => item.Entry.RunId == runId);
        return Task.FromResult(line is null ? null : new LedgerResult(line.Entry, Report: null));
    }

    public Task<LedgerEntry> DecideAsync(Func<IReadOnlyList<LedgerEntry>, Decision> decide, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<LedgerEntry> DecideAsync(Func<IReadOnlyList<LedgerEntry>, Decision> decide, RunAssignment? assignment, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
