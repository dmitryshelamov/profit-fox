using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     An idea with the statistics of its bucket over 90 days and over the last 30, none while the bucket has no case. <see cref="Shares" /> is
///     its money on 1 USDT of amount by the fees of its category, with its break-even.
/// </summary>
public sealed record IdeaView(Idea Idea, BucketStatistics? Bucket, BucketStatistics? Recent, IdeaShares Shares)
{
    /// <summary>The probability of the bucket, none while the bucket has too few cases to show it.</summary>
    public decimal? Probability => Bucket is { Cases: >= BucketStatisticsCalculator.MinimumCases } ? Bucket.Probability : null;

    public static IReadOnlyList<IdeaView> Of(IEnumerable<Idea> ideas, IReadOnlyList<IdeaBucket> buckets, IReadOnlyDictionary<MarketCategory, FeeRates> fees)
    {
        ILookup<(IdeaBucketKey Key, int Days), IdeaBucket> byKey = buckets.ToLookup(bucket => (bucket.Key, bucket.Days));

        return
        [
            .. ideas.Select(idea =>
            {
                var key = new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, idea.Instrument.Category);

                return new IdeaView(idea, byKey[(key, 90)].FirstOrDefault()?.Statistics, byKey[(key, 30)].FirstOrDefault()?.Statistics,
                    IdeaMoney.PerUsdt(idea.Track.Plan, fees[idea.Instrument.Category]));
            })
        ];
    }
}