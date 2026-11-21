using ProfitFox.Application.Hypotheses;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Ledger;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Runs;

/// <summary>
///     Puts runs in the queue and takes them out (spec bench-run-queue «Постановка в очередь», «Отмена прогона»). The rules of
///     the ledger are checked when a run is queued, so a refusal comes at once and leaves no line; the Worker checks them
///     again when the run is decided.
/// </summary>
public sealed class RunScheduler(HypothesisCatalog hypotheses, IRunLedger ledger, IRunQueue queue)
{
    /// <summary>
    ///     Queues a run of the version <paramref name="version" /> of a hypothesis (its latest when null) on a period.
    /// </summary>
    /// <exception cref="HypothesisException">The hypothesis or its version is not there, or cannot be run.</exception>
    /// <exception cref="LedgerException">The rules of the ledger refuse the run, or the same run already waits or goes; <see cref="LedgerException.RunId" /> is the run that stands in the way.</exception>
    public async Task<RunRecord> Enqueue(string hypothesis, int? version, Period period, bool parity, CancellationToken cancellation)
    {
        Hypothesis read = await hypotheses.Read(hypothesis, version, cancellation);
        string name = Periods.Name(period);
        LedgerRules.CheckCanRun(await ledger.Entries(cancellation), read.Id, read.Version, name, parity);
        return await queue.Enqueue(Guid.NewGuid().ToString("N"), read.Id, read.Version, name, parity, cancellation);
    }

    public Task<CancelOutcome> Cancel(string runId, CancellationToken cancellation)
    {
        return queue.Cancel(runId, cancellation);
    }
}
