using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     The out-of-sample check of a candidate exit rule (decision 1 of check-idea-exit-rules-out-of-sample): the selection from
///     <see cref="SelectionFrom" /> to <see cref="CheckFrom" />, the check from it to <see cref="To" />, the <see cref="Rules" /> of the grid, the
///     candidate and the best rule of the grid on the selection with their numbers on both parts, and the probability of overfitting of the grid.
/// </summary>
public sealed record ExitRuleOutOfSample(
    DateTimeOffset SelectionFrom,
    DateTimeOffset CheckFrom,
    DateTimeOffset To,
    int Rules,
    ExitRuleParts Candidate,
    ExitRuleBest? Best,
    Overfitting? Overfitting);

/// <summary>The clustered numbers of the entries of a rule on the selection and on the check; none without an entry.</summary>
public sealed record ExitRuleParts(BucketStatistics? Selection, BucketStatistics? Check);

/// <summary>The best rule of the grid on the selection and its numbers.</summary>
public sealed record ExitRuleBest(IdeaExitRule Rule, ExitRuleParts Parts);