using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     An idea with the statistics of its bucket over 90 days and over the last 30, none while the bucket has no case. <see cref="Shares" /> is
///     its money on 1 USDT of amount by the fees of its category, with its break-even; <see cref="Numbers" /> its own numbers (decision 5 of
///     add-own-idea-numbers), from its bucket over 90 days and the curve of «Уровни» of its interval, side and category, which every reader of
///     the probability, the expectancy and the badge of an idea takes.
/// </summary>
public sealed record IdeaView(Idea Idea, BucketStatistics? Bucket, BucketStatistics? Recent, IdeaShares Shares, IdeaNumbers Numbers)
{
    public static IReadOnlyList<IdeaView> Of(
        IEnumerable<Idea> ideas,
        IReadOnlyList<IdeaBucket> buckets,
        IReadOnlyList<RiskRewardCurve> curves,
        IReadOnlyDictionary<MarketCategory, FeeRates> fees)
    {
        ILookup<(IdeaBucketKey Key, int Days), IdeaBucket> byKey = buckets.ToLookup(bucket => (bucket.Key, bucket.Days));

        return
        [
            .. ideas.Select(idea =>
            {
                IdeaPlan plan = idea.Track.Plan;
                var key = new IdeaBucketKey(idea.Setup, plan.Type, idea.Interval, plan.Side, idea.Instrument.Category);
                BucketStatistics? bucket = byKey[(key, 90)].FirstOrDefault()?.Statistics;
                FeeRates rates = fees[idea.Instrument.Category];
                RiskRewardCurve? curve = curves.FirstOrDefault(found => found.Interval == idea.Interval && found.Side == plan.Side && found.Category == idea.Instrument.Category);

                return new IdeaView(idea, bucket, byKey[(key, 30)].FirstOrDefault()?.Statistics, IdeaMoney.PerUsdt(plan, rates),
                    IdeaNumbersCalculator.Compute(plan, rates, bucket, curve));
            })
        ];
    }
}