namespace ProfitFox.Application.Contracts.Runs;

/// <summary>
///     Whether a version of a hypothesis can be run on a period in a kind now, and if not, why: the rules of the ledger, or a
///     run of the same version, period and kind that waits or goes. <see cref="RunId" /> is the run or the ledger line that
///     stands in the way, when there is one.
/// </summary>
public sealed record RunAvailability(string Period, string Kind, bool CanRun, string? Reason, string? RunId);
