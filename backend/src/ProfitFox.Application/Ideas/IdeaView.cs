using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     An idea with the statistics of its bucket over 90 days and over the last 30, none while the bucket has no case; the baseline is the
///     bucket's, or the idea's own 1 / (1 + R:R) without one.
/// </summary>
public sealed record IdeaView(Idea Idea, BucketStatistics? Bucket, BucketStatistics? Recent)
{
    public decimal Baseline => Bucket?.Baseline ?? (1m / (1m + Idea.Track.Plan.RiskReward));

    public static IReadOnlyList<IdeaView> Of(IEnumerable<Idea> ideas, IReadOnlyList<IdeaBucket> buckets)
    {
        ILookup<(IdeaBucketKey Key, int Days), IdeaBucket> byKey = buckets.ToLookup(bucket => (bucket.Key, bucket.Days));

        return
        [
            .. ideas.Select(idea =>
            {
                var key = new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, idea.Instrument.Category);

                return new IdeaView(idea, byKey[(key, 90)].FirstOrDefault()?.Statistics, byKey[(key, 30)].FirstOrDefault()?.Statistics);
            })
        ];
    }
}