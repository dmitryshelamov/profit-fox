using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>What one pass over the signals gives: the buckets of 90 and 30 days and how each counted case ended, for the calibration.</summary>
public sealed record IdeaOutcomesResult(IReadOnlyList<IdeaBucket> Buckets, IReadOnlyList<IdeaOutcome> Outcomes);