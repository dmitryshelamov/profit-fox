namespace ProfitFox.Application.Contracts.Runs;

/// <summary>
///     The last run of the latest version of a hypothesis (spec bench-run-queue «Последний прогон гипотезы»): one of the two
///     is given — the run of the queue that waits or goes, or the line of the run that ended last.
/// </summary>
public sealed record LastRunInternal(RunInternal? Active, FinishedRunInternal? Finished);
