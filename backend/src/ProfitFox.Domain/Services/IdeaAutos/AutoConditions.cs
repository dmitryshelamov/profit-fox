using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>Whether an idea fits the conditions of an auto. A misfit is not a refusal: the idea is passed over without a trace.</summary>
public static class AutoConditions
{
    /// <param name="idea"></param>
    /// <param name="bucket">The statistics of the bucket of the idea over 90 days, none without a case, whose cases the minimum counts.</param>
    /// <param name="badge">The badge of the idea itself (decision 5 of add-own-idea-numbers), none without one.</param>
    /// <param name="conditions"></param>
    public static bool Match(Idea idea, BucketStatistics? bucket, IdeaBadge? badge, IdeaAutoConditions conditions)
    {
        return conditions.Setups.Contains(idea.Setup)
               && conditions.Types.Contains(idea.Track.Plan.Type)
               && conditions.Intervals.Contains(idea.Interval)
               && (conditions.Side is null || conditions.Side == idea.Track.Plan.Side)
               && (conditions.Category is null || conditions.Category == idea.Instrument.Category)
               && (bucket?.Cases ?? 0) >= conditions.MinCases
               && BadgeFits(badge, conditions.Badge)
               && FeatureFits(idea.Features.DayDrop, conditions.DayDrop)
               && FeatureFits(idea.Features.BtcDrop, conditions.BtcDrop)
               && FeatureFits(idea.Features.HighFunding, conditions.HighFunding)
               && FeatureFits(idea.Features.Burst, conditions.Burst);
    }

    private static bool FeatureFits(bool present, FeatureCondition condition)
    {
        return condition switch
        {
            FeatureCondition.With => present,
            FeatureCondition.Without => !present,
            _ => true
        };
    }

    // ▲ > ≈ > ▼ > none: «любой» takes all of them, ▼ every badge there is.
    private static bool BadgeFits(IdeaBadge? badge, IdeaBadge? chosen)
    {
        return chosen is null || Rank(badge) >= Rank(chosen);
    }

    private static int Rank(IdeaBadge? badge)
    {
        return badge switch
        {
            IdeaBadge.Up => 3,
            IdeaBadge.Flat => 2,
            IdeaBadge.Down => 1,
            _ => 0
        };
    }
}