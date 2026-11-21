using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Tests.Unit.Application.Queries.Runs;

/// <summary>
///     The queue of runs in memory, for the queries that only read it: the runs a test put there, in the order it did. The
///     steps of a Worker are the job of the database repository.
/// </summary>
public sealed class MemoryRunRepository : IRunRepository
{
    private readonly List<Run> _runs = [];

    /// <summary>
    ///     How many times the queue was read.
    /// </summary>
    public int ActiveReads { get; private set; }

    public void Add(Run run)
    {
        _runs.Add(run);
    }

    public Task<IReadOnlyList<Run>> ActiveAsync(CancellationToken cancellationToken)
    {
        ActiveReads++;
        return Task.FromResult<IReadOnlyList<Run>>([.. _runs.Where(run => run.Status != RunState.Finished)]);
    }

    public Task<Run?> FindAsync(string runId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_runs.FirstOrDefault(run => run.Id == runId));
    }

    public Task<Run> EnqueueAsync(string id, string hypothesis, int version, string period, bool parity, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<Run?> ClaimAsync(string worker, TimeSpan lease, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<Renewal?> RenewAsync(string runId, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<CancelOutcome> CancelAsync(string runId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
