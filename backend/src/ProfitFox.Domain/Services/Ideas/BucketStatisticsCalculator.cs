using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The statistics of a bucket from the counts of its cases, their total in R and the total of the squares of their results, sums that a
///     window of cases can add and take away (decision 1).
/// </summary>
public static class BucketStatisticsCalculator
{
    /// <summary>Under this many cases a bucket has too little data for a share.</summary>
    public const int MinimumCases = 30;

    // The 97.5th percentile of the standard normal distribution: a 95 % interval.
    private const double Z = 1.96;

    /// <param name="targets">The cases that reached the target.</param>
    /// <param name="stops">The cases that reached the stop.</param>
    /// <param name="expired">The cases that expired.</param>
    /// <param name="totalR">ΣR of the cases.</param>
    /// <param name="totalSquaredR">ΣR² of the cases.</param>
    /// <param name="signals">The signals counted, entered or not, of a type that waits for its entry.</param>
    public static BucketStatistics Compute(int targets, int stops, int expired, decimal totalR, decimal totalSquaredR, int? signals = null)
    {
        int cases = targets + stops + expired;
        decimal expectancy = totalR / cases;

        if (cases < MinimumCases)
        {
            return new BucketStatistics(cases, targets, stops, expired, null, null, null, expectancy, null, null, signals);
        }

        (double low, double high) = Wilson((double)targets / cases, cases);

        // The sample variance; sums rounded to 12 places may leave it a hair below zero when the results are equal.
        decimal variance = Math.Max(0m, (totalSquaredR - (totalR * totalR / cases)) / (cases - 1));
        var margin = (decimal)(Z * Math.Sqrt((double)(variance / cases)));

        return new BucketStatistics(cases, targets, stops, expired, (decimal)targets / cases, (decimal)low, (decimal)high,
            expectancy, expectancy - margin, expectancy + margin, signals);
    }

    /// <summary>The 95 % Wilson score interval of a share of <paramref name="cases" />; a share is no money, so its square root may go through double.</summary>
    internal static (double Low, double High) Wilson(double share, double cases)
    {
        double spread = Z * Z / cases;
        double centre = (share + (spread / 2)) / (1 + spread);
        double half = Z * Math.Sqrt((share * (1 - share) / cases) + (spread / (4 * cases))) / (1 + spread);

        return (centre - half, centre + half);
    }
}