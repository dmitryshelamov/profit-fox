using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     What one pass over the signals gives: the buckets of 90 and 30 days, how each counted case ended, for the calibration, none when the pass
///     counted more cases than it may keep, the roots of the categories the buckets were shrunk to, by the days of the window, and the curves of
///     «Уровни» by the reward to risk over 90 days (decision 4 of add-own-idea-numbers).
/// </summary>
public sealed record IdeaOutcomesResult(IReadOnlyList<IdeaBucket> Buckets, IReadOnlyList<IdeaOutcome>? Outcomes, IdeaRoots Roots, IReadOnlyList<RiskRewardCurve> Curves);

/// <summary>
///     The roots of the categories by the days of the window: those of the model and those of its second run over the results without costs
///     (decision 3 of add-own-idea-numbers), which the recompute of a type × interval shrinks to.
/// </summary>
public sealed record IdeaRoots(
    IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>> Net,
    IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>> Gross);