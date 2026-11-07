using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     The out-of-sample check of a candidate exit rule (decisions 1, 7 and 8 of check-idea-exit-rules-out-of-sample): its window, the
///     <see cref="Rules" /> of the grid, the candidate and the best rule of the grid on the selection with their numbers on both parts, and the
///     probability of overfitting of the grid.
/// </summary>
public sealed record ExitRuleOutOfSample(OutOfSampleWindow Window, int Rules, ExitRuleParts Candidate, ExitRuleBest? Best, Overfitting? Overfitting);

/// <summary>
///     The clustered numbers of the entries of a rule on the selection, which ends at <see cref="SelectionTo" />, the horizon of the rule before
///     the check, and on the check; none without an entry.
/// </summary>
public sealed record ExitRuleParts(DateTimeOffset SelectionTo, BucketStatistics? Selection, BucketStatistics? Check);

/// <summary>The best rule of the grid on the selection and its numbers.</summary>
public sealed record ExitRuleBest(IdeaExitRule Rule, ExitRuleParts Parts);