using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The sums of a group of cases by the blocks UTC they fired in (decision 1 of honest-idea-intervals): the counts of the outcomes, ΣR and ΣR²,
///     and over the blocks with a case, the groups, G of them, each with n_g cases, the total S_g of their results and T_g of their targets:
///     Σn_g², Σn_g·S_g, ΣS_g², Σn_g·T_g and ΣT_g². A case added and then removed gives the sums back, so a window of cases can slide. The blocks
///     follow one another from the Unix epoch, each as long as the holding of the cases (decision 1 of cluster-idea-cases-by-holding); few of
///     them widen the variances by the Student quantile of G − 1 degrees of freedom (decision 6 of it).
/// </summary>
/// <param name="block">The length of a block.</param>
public sealed class ClusteredSums(TimeSpan block)
{
    // The 97.5th percentile of the standard normal distribution: a 95 % interval.
    private const double Z = 1.96;

    // The same percentile to the precision of the expansion of the Student quantile.
    private const double ExactZ = 1.959963984540054;

    // The 97.5th percentiles of the Student distribution of 1 to 9 degrees of freedom, where the expansion is off by more than 10⁻⁵.
    private static readonly double[] StudentQuantiles =
        [12.706204736174694, 4.302652729749462, 3.1824463052837078, 2.7764451051977934, 2.5705818356363146, 2.4469118511449786, 2.364624251592784, 2.306004135204166, 2.262157162798205];

    private readonly Dictionary<long, Group> _groups = [];

    public int Targets { get; private set; }

    public int Stops { get; private set; }

    public int Expired { get; private set; }

    public int Cases => Targets + Stops + Expired;

    public decimal TotalR { get; private set; }

    public decimal TotalSquaredR { get; private set; }

    /// <summary>G, the blocks with a case.</summary>
    public int Groups => _groups.Count;

    /// <summary>Σn_g².</summary>
    public long SquaredCounts { get; private set; }

    /// <summary>Σn_g·S_g.</summary>
    public decimal CountsByResults { get; private set; }

    /// <summary>ΣS_g².</summary>
    public decimal SquaredResults { get; private set; }

    /// <summary>Σn_g·T_g.</summary>
    public long CountsByTargets { get; private set; }

    /// <summary>ΣT_g².</summary>
    public long SquaredTargets { get; private set; }

    /// <summary>
    ///     The variance of the mean result: V_r = G / (G − 1) · (ΣS_g² − 2m·Σn_g·S_g + m²·Σn_g²) / n² over the blocks, times (t(G − 1) / 1.96)²
    ///     of the Student quantile of G − 1 degrees of freedom, never below s² / n of independent cases; s², as of one case, with fewer than two
    ///     blocks.
    /// </summary>
    public double ExpectancyVariance => Variance((double)SampleVariance(), SquaredResults, CountsByResults, TotalR);

    /// <summary>The variance of the share of targets by the same formula on the targets of the blocks, p(1 − p) taking the place of s².</summary>
    public double ShareVariance
    {
        get
        {
            double share = (double)Targets / Cases;

            return Variance(share * (1 - share), SquaredTargets, CountsByTargets, Targets);
        }
    }

    /// <summary>
    ///     The own numbers of the cases: the share of targets among all of them with its 95 % Wilson interval over the effective cases
    ///     n_eff = p(1 − p) / V_p, and the mean result with its 95 % interval m ± 1.96·√V; under 30 cases only the counts and the mean.
    /// </summary>
    public BucketStatistics Statistics()
    {
        decimal expectancy = TotalR / Cases;

        if (Cases < BucketStatisticsCalculator.MinimumCases)
        {
            return new BucketStatistics(Cases, Targets, Stops, Expired, null, null, null, expectancy, null, null);
        }

        double share = (double)Targets / Cases;
        double shareVariance = ShareVariance;
        // A share of 0 or 1 has no spread to widen: its cases count as they are.
        double effective = shareVariance > 0 ? share * (1 - share) / shareVariance : Cases;
        (double low, double high) = BucketStatisticsCalculator.Wilson(share, effective);
        var margin = (decimal)(Z * Math.Sqrt(ExpectancyVariance));

        return new BucketStatistics(Cases, Targets, Stops, Expired, (decimal)Targets / Cases, (decimal)low, (decimal)high, expectancy,
            expectancy - margin, expectancy + margin);
    }

    public void Add(DateTimeOffset firedAt, IdeaStatus status, decimal result)
    {
        Change(firedAt, status, result, 1);
    }

    public void Remove(DateTimeOffset firedAt, IdeaStatus status, decimal result)
    {
        Change(firedAt, status, result, -1);
    }

    private void Change(DateTimeOffset firedAt, IdeaStatus status, decimal result, int sign)
    {
        switch (status)
        {
            case IdeaStatus.Target:
                Targets += sign;
                break;
            case IdeaStatus.Stop:
                Stops += sign;
                break;
            default:
                Expired += sign;
                break;
        }

        TotalR += sign * result;
        TotalSquaredR += sign * result * result;

        long number = (firedAt - DateTimeOffset.UnixEpoch).Ticks / block.Ticks;
        Group before = _groups.GetValueOrDefault(number);
        var after = new Group(before.Cases + sign, before.Results + (sign * result), before.Targets + (status == IdeaStatus.Target ? sign : 0));

        // The aggregates move by the difference of the contribution of the one group that changed.
        SquaredCounts += ((long)after.Cases * after.Cases) - ((long)before.Cases * before.Cases);
        CountsByResults += (after.Cases * after.Results) - (before.Cases * before.Results);
        SquaredResults += (after.Results * after.Results) - (before.Results * before.Results);
        CountsByTargets += ((long)after.Cases * after.Targets) - ((long)before.Cases * before.Targets);
        SquaredTargets += ((long)after.Targets * after.Targets) - ((long)before.Targets * before.Targets);

        if (after.Cases == 0)
        {
            _groups.Remove(number);
        }
        else
        {
            _groups[number] = after;
        }
    }

    // The sample variance s²; the sums are exact in memory, not rounded as in storage.
    private decimal SampleVariance()
    {
        return (TotalSquaredR - (TotalR * TotalR / Cases)) / (Cases - 1);
    }

    private double Variance(double caseVariance, decimal squaredSums, decimal countsBySums, decimal total)
    {
        if (Groups < 2)
        {
            return caseVariance;
        }

        decimal mean = total / Cases;
        decimal spread = squaredSums - (2 * mean * countsBySums) + (mean * mean * SquaredCounts);
        double clustered = (double)Groups / (Groups - 1) * (double)spread / ((double)Cases * Cases);
        // Few groups give a loose estimate: the interval of ±1.96 √V is to be as wide as ± t(G − 1) of the clustered variance (review of
        // cluster-idea-cases-by-holding), so the variance grows by (t / 1.96)².
        double student = StudentQuantile(Groups - 1) / Z;

        return Math.Max(clustered * student * student, caseVariance / Cases);
    }

    // The 97.5th percentile of the Student distribution: the table up to 9 degrees of freedom, the Cornish–Fisher expansion from 10.
    private static double StudentQuantile(int degrees)
    {
        if (degrees < 10)
        {
            return StudentQuantiles[degrees - 1];
        }

        double z = ExactZ;
        double v = degrees;

        return z
               + ((Math.Pow(z, 3) + z) / (4 * v))
               + (((5 * Math.Pow(z, 5)) + (16 * Math.Pow(z, 3)) + (3 * z)) / (96 * v * v))
               + (((3 * Math.Pow(z, 7)) + (19 * Math.Pow(z, 5)) + (17 * Math.Pow(z, 3)) - (15 * z)) / (384 * Math.Pow(v, 3)))
               + (((79 * Math.Pow(z, 9)) + (776 * Math.Pow(z, 7)) + (1482 * Math.Pow(z, 5)) - (1920 * Math.Pow(z, 3)) - (945 * z)) / (92160 * Math.Pow(v, 4)));
    }

    private readonly record struct Group(int Cases, decimal Results, int Targets);
}