using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     What a candidate exit rule would have done (decision 8): «было → станет» over the last 90 and 30 days, the expectancy at every lifetime of
///     the grid and the curve of the entries that reached each level of the ladder.
/// </summary>
public sealed record ExitRulePreview(ExitRuleWindow Last90, ExitRuleWindow Last30, IReadOnlyList<ExitRuleLife> Lives, IReadOnlyList<ExitRuleMfe> Mfe);

/// <summary>The statistics of all the signals of a window by the rule now and by the candidate; none without an entry.</summary>
public sealed record ExitRuleWindow(BucketStatistics? Current, BucketStatistics? Candidate);

/// <summary>The expectancy per entry and per signal over 90 days at a lifetime, by the target and the window of the candidate.</summary>
public sealed record ExitRuleLife(int Candles, decimal? Expectancy, decimal? SignalExpectancy);

/// <summary>
///     A point of the curve of MFE: the share of the entries of the candidate over 90 days that reached +<see cref="R" /> before the stop and the
///     end of its lifetime, and the break-even of a target of <see cref="R" />, the mean over them.
/// </summary>
public sealed record ExitRuleMfe(decimal R, decimal Reached, decimal BreakEven);