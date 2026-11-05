using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>The statistics of a bucket from the counts of its cases, their mean R:R and their total in R.</summary>
public static class BucketStatisticsCalculator
{
    /// <summary>Under this many cases a bucket has too little data for a share.</summary>
    public const int MinimumCases = 30;

    // The 97.5th percentile of the standard normal distribution: a 95 % interval.
    private const double Z = 1.96;

    public static BucketStatistics Compute(int targets, int stops, int expired, decimal averageRiskReward, decimal totalR)
    {
        int cases = targets + stops + expired;
        decimal baseline = 1m / (1m + averageRiskReward);
        decimal expectancy = totalR / cases;

        if (cases < MinimumCases || targets + stops == 0)
        {
            return new BucketStatistics(cases, expired, null, null, null, baseline, expectancy);
        }

        // Wilson score interval; a share is no money, so its square root may go through double.
        int decided = targets + stops;
        double share = (double)targets / decided;
        double spread = Z * Z / decided;
        double centre = (share + (spread / 2)) / (1 + spread);
        double half = Z * Math.Sqrt((share * (1 - share) / decided) + (spread / (4 * decided))) / (1 + spread);

        return new BucketStatistics(cases, expired, (decimal)targets / decided, (decimal)(centre - half), (decimal)(centre + half), baseline, expectancy);
    }
}