using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Ports.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Presentation.Runs;

/// <summary>
///     The runs as the API tells them.
/// </summary>
public static class RunResponses
{
    /// <summary>
    ///     A run of the queue that waits or goes, or one just queued.
    /// </summary>
    public static RunResponse Of(RunRecord run)
    {
        return Of(run, entry: null, report: null);
    }

    /// <summary>
    ///     A line of the list of finished runs, without its report.
    /// </summary>
    public static RunResponse Of(FinishedRun finished)
    {
        LedgerEntry entry = finished.Entry;
        return Finished(entry, finished.Parity, report: null);
    }

    /// <summary>
    ///     One run with its row in the queue and, once it ended, its line and report in the ledger.
    /// </summary>
    public static RunResponse Of(RunDetails details)
    {
        return Of(details.Run, details.Entry, details.Report);
    }

    private static RunResponse Of(RunRecord run, LedgerEntry? entry, string? report)
    {
        return new RunResponse(
            run.Id,
            run.Hypothesis,
            run.Version,
            run.Period,
            RunKinds.Name(run.Parity),
            run.Status,
            run.QueuedAt,
            run.StartedAt,
            run.FinishedAt,
            run.Status == RunState.Running ? run.LeaseOwner : null,
            run.PairsDone,
            run.PairsTotal,
            run.LostLeases,
            run.CancelRequested,
            entry is null ? null : Result(entry, report));
    }

    private static RunResponse Finished(LedgerEntry entry, bool parity, string? report)
    {
        return new RunResponse(
            entry.RunId,
            entry.Hypothesis,
            entry.Version,
            entry.Period,
            RunKinds.Name(parity),
            RunState.Finished,
            QueuedAt: null,
            StartedAt: null,
            entry.Time,
            Worker: null,
            PairsDone: 0,
            PairsTotal: null,
            LostLeases: 0,
            CancelRequested: false,
            Result(entry, report));
    }

    private static RunResultResponse Result(LedgerEntry entry, string? report)
    {
        return new RunResultResponse(
            entry.Time,
            entry.Status,
            entry.Reason,
            entry.Hash,
            entry.Version,
            entry.EngineVersion,
            entry.DataHash,
            entry.Attempts,
            entry.RequiredT,
            entry.Power,
            entry.Blocks,
            entry.Events,
            entry.T,
            entry.Mean,
            entry.FirstHalfMean,
            entry.SecondHalfMean,
            entry.ReadSeconds,
            entry.Corrects,
            report);
    }
}
