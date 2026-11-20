namespace ProfitFox.Bench.Presentation.Runs;

/// <summary>
///     A run to put in the queue: the hypothesis, its version (the latest when missing), the period
///     (<c>exploration</c>, <c>holdout</c> or <c>new-data</c>) and the kind (<c>verdict</c> or <c>parity</c>).
/// </summary>
public sealed record CreateRunRequest(string? Hypothesis, int? Version, string? Period, string? Kind);
