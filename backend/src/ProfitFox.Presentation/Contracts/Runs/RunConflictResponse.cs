namespace ProfitFox.Presentation.Contracts.Runs;

/// <summary>
///     Why a run was refused or cannot be cancelled, and the run or the ledger line that stands in the way, when there is one.
/// </summary>
public sealed record RunConflictResponse(string Reason, string? RunId);
