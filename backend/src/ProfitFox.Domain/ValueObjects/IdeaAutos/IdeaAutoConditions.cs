using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>
///     What an idea must be for an auto to take it: any of the marked setups, types and intervals, the side and the market when one is chosen
///     (none is both), a badge of its bucket not worse than <see cref="Badge" /> (none is any, even none at all) and at least
///     <see cref="MinCases" /> cases in the bucket, and each feature of the market of the idea as asked (decision 8 of add-market-idea-features), the burst among them.
///     <see cref="MinRiskReward" /> is not a condition: it is a check of the entry.
/// </summary>
public sealed record IdeaAutoConditions(
    IReadOnlyCollection<IdeaSetup> Setups,
    IReadOnlyCollection<IdeaType> Types,
    IReadOnlyCollection<Interval> Intervals,
    IdeaSide? Side,
    MarketCategory? Category,
    IdeaBadge? Badge,
    decimal MinRiskReward,
    int MinCases,
    FeatureCondition DayDrop = FeatureCondition.Any,
    FeatureCondition BtcDrop = FeatureCondition.Any,
    FeatureCondition HighFunding = FeatureCondition.Any,
    FeatureCondition Burst = FeatureCondition.Any);