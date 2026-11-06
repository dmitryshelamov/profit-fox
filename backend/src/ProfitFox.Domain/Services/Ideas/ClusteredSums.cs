using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The sums of a group of cases by the hours UTC they fired in (decision 1 of honest-idea-intervals): the counts of the outcomes, ΣR and ΣR²,
///     and over the hours with a case, the groups, G of them, each with n_g cases, the total S_g of their results and T_g of their targets:
///     Σn_g², Σn_g·S_g, ΣS_g², Σn_g·T_g and ΣT_g². A case added and then removed gives the sums back, so a window of cases can slide.
/// </summary>
public sealed class ClusteredSums
{
    // The 97.5th percentile of the standard normal distribution: a 95 % interval.
    private const double Z = 1.96;

    private readonly Dictionary<DateTimeOffset, Group> _groups = [];

    public int Targets { get; private set; }

    public int Stops { get; private set; }

    public int Expired { get; private set; }

    public int Cases => Targets + Stops + Expired;

    public decimal TotalR { get; private set; }

    public decimal TotalSquaredR { get; private set; }

    /// <summary>G, the hours with a case.</summary>
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
    ///     The variance of the mean result: V_r = G / (G − 1) · (ΣS_g² − 2m·Σn_g·S_g + m²·Σn_g²) / n² over the hours, never below s² / n of
    ///     independent cases; s², as of one case, with fewer than two hours.
    /// </summary>
    public double ExpectancyVariance => Variance((double)SampleVariance(), SquaredResults, CountsByResults, TotalR);

    /// <summary>The variance of the share of targets by the same formula on the targets of the hours, p(1 − p) taking the place of s².</summary>
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

        var hour = new DateTimeOffset(firedAt.UtcDateTime.Ticks - (firedAt.UtcDateTime.Ticks % TimeSpan.TicksPerHour), TimeSpan.Zero);
        Group before = _groups.GetValueOrDefault(hour);
        var after = new Group(before.Cases + sign, before.Results + (sign * result), before.Targets + (status == IdeaStatus.Target ? sign : 0));

        // The aggregates move by the difference of the contribution of the one group that changed.
        SquaredCounts += ((long)after.Cases * after.Cases) - ((long)before.Cases * before.Cases);
        CountsByResults += (after.Cases * after.Results) - (before.Cases * before.Results);
        SquaredResults += (after.Results * after.Results) - (before.Results * before.Results);
        CountsByTargets += ((long)after.Cases * after.Targets) - ((long)before.Cases * before.Targets);
        SquaredTargets += ((long)after.Targets * after.Targets) - ((long)before.Targets * before.Targets);

        if (after.Cases == 0)
        {
            _groups.Remove(hour);
        }
        else
        {
            _groups[hour] = after;
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

        return Math.Max(clustered, caseVariance / Cases);
    }

    private readonly record struct Group(int Cases, decimal Results, int Targets);
}