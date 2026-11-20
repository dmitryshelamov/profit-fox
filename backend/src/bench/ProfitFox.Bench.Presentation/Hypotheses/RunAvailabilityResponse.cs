namespace ProfitFox.Bench.Presentation.Hypotheses;

/// <summary>
///     Whether a hypothesis can be run on a period in a kind now (spec hypothesis-bench), for the buttons of the page. When it
///     cannot, <see cref="Reason" /> is the text the run would be refused with, and <see cref="RunId" /> the run or the
///     ledger line that stands in the way, when there is one. <see cref="Kind" /> is <c>verdict</c> or <c>parity</c>.
/// </summary>
public sealed record RunAvailabilityResponse(string Period, string Kind, bool CanRun, string? Reason, string? RunId);
