using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The features of the market on the minute a signal fired (decision 1 of add-market-idea-features): a drop of the pair over a day, a drop
///     of BTC over 4 hours and a high funding rate of the pair, and a burst (decision 1 of add-burst-and-auto-limits): signals of the same side
///     on the same interval by many distinct pairs in the minute; their sixteen combinations are the levels the cases of a bucket split into. A
///     change is a share of the close a span before, <c>close_now / close_before − 1</c>; a number that is not known gives no feature.
/// </summary>
public readonly record struct IdeaFeatures(bool DayDrop, bool BtcDrop, bool HighFunding, bool Burst = false)
{
    /// <summary>A change of the pair over a day of a day drop, up to which it counts.</summary>
    private const decimal DayDropUpTo = -0.05m;

    /// <summary>A change of BTC over 4 hours of a BTC drop, up to which it counts.</summary>
    private const decimal BtcDropUpTo = -0.01m;

    /// <summary>A funding rate of a high funding, from which it counts.</summary>
    private const decimal HighFundingFrom = 0.0005m;

    /// <summary>
    ///     The features of the numbers of a signal; <paramref name="burstPairs" /> is the count of the distinct pairs with a signal of its side on
    ///     its interval in its minute, its own pair among them, none where it is not known.
    /// </summary>
    public static IdeaFeatures Of(decimal? pairChange, decimal? btcChange, decimal? funding, int? burstPairs, Interval interval)
    {
        return new IdeaFeatures(pairChange <= DayDropUpTo, btcChange <= BtcDropUpTo, funding >= HighFundingFrom, burstPairs >= BurstThreshold(interval));
    }

    /// <summary>The count of the distinct pairs of a burst on an interval of ideas, from which it counts: about the 90th percentile of prod.</summary>
    public static int BurstThreshold(Interval interval)
    {
        return interval switch
        {
            Interval.FiveMinutes => 60,
            Interval.FifteenMinutes => 25,
            Interval.OneHour => 8,
            _ => 4
        };
    }

    public bool Has(IdeaFeature feature)
    {
        return feature switch
        {
            IdeaFeature.DayDrop => DayDrop,
            IdeaFeature.BtcDrop => BtcDrop,
            IdeaFeature.HighFunding => HighFunding,
            _ => Burst
        };
    }
}