using System.Globalization;

namespace ProfitFox.Bench.Domain.Verdicts;

/// <summary>
///     The verdict on block means of «event minus random entry» (spec hypothesis-bench), by <c>verdict.py</c> of the Python
///     bench: the required t grows with the attempts (Bonferroni), power is estimated blind from random entries, and the
///     outcomes of events are read only when the power is enough.
/// </summary>
public static class VerdictRules
{
    public const double BaseT = 3.0;
    public const double RequiredPower = 0.8;
    public const int MinBlocks = 20;

    /// <summary>
    ///     The t needed with <paramref name="attempts" /> hypotheses tested in the period, this one included.
    /// </summary>
    public static double RequiredT(int attempts)
    {
        attempts = Math.Max(1, attempts);
        double tail = (1 - NormalDistribution.Cdf(BaseT)) / attempts;
        return Math.Max(BaseT, NormalDistribution.InverseCdf(1 - tail));
    }

    /// <summary>
    ///     The chance that a true effect of <paramref name="effect" /> per block mean gives t ≥ <paramref name="tNeeded" />.
    /// </summary>
    public static double Power(double effect, double sd, int blocks, double tNeeded)
    {
        if (blocks < 2 || !(sd > 0))
        {
            return 0;
        }

        return NormalDistribution.Cdf(effect * Math.Sqrt(blocks) / sd - tNeeded);
    }

    public static double TStatistic(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return double.NaN;
        }

        double sd = StandardDeviation(values);
        double mean = values.Average();
        return sd > 0 ? mean / (sd / Math.Sqrt(values.Count)) : mean >= 0 ? double.PositiveInfinity : double.NegativeInfinity;
    }

    /// <summary>
    ///     (power, sd of the pseudo block means, required t), from random entries only.
    /// </summary>
    public static (double Power, double Sd, double RequiredT) PlannedPower(IReadOnlyList<double> pseudoBlockMeans, double minEffect, int attempts)
    {
        double tNeeded = RequiredT(attempts);
        double[] values = [.. pseudoBlockMeans.Where(value => !double.IsNaN(value))];
        double sd = values.Length >= 2 ? StandardDeviation(values) : double.NaN;
        return (Power(minEffect, sd, values.Length, tNeeded), sd, tNeeded);
    }

    public static bool EnoughPower(IReadOnlyList<double> pseudoBlockMeans, double minEffect, int attempts)
    {
        (double power, _, _) = PlannedPower(pseudoBlockMeans, minEffect, attempts);
        return pseudoBlockMeans.Count(value => !double.IsNaN(value)) >= MinBlocks && power >= RequiredPower;
    }

    /// <summary>
    ///     The verdict. <paramref name="blockMeans" /> are in time order and are read only when the power is enough: pass
    ///     null to stay blind.
    /// </summary>
    public static Verdict Decide(
        IReadOnlyList<double>? blockMeans,
        IReadOnlyList<double> pseudoBlockMeans,
        double minEffect,
        int attempts,
        int events)
    {
        (double power, double sd, double tNeeded) = PlannedPower(pseudoBlockMeans, minEffect, attempts);
        int blocks = pseudoBlockMeans.Count(value => !double.IsNaN(value));
        if (blocks < MinBlocks)
        {
            return new Verdict(VerdictKind.InsufficientData, $"{blocks} blocks, need {MinBlocks}", attempts, tNeeded, minEffect, blocks, events, power, sd);
        }

        if (power < RequiredPower)
        {
            return new Verdict(
                VerdictKind.InsufficientData,
                string.Create(CultureInfo.InvariantCulture, $"power {power:F2} < {RequiredPower} for effect {minEffect}"),
                attempts,
                tNeeded,
                minEffect,
                blocks,
                events,
                power,
                sd);
        }

        if (blockMeans is null)
        {
            throw new ArgumentException("The power is enough: the block means are needed to decide.", nameof(blockMeans));
        }

        double[] values = [.. blockMeans.Where(value => !double.IsNaN(value))];
        int half = values.Length / 2;
        double first = values[..half].Average();
        double second = values[half..].Average();
        double t = TStatistic(values);
        Verdict result = new Verdict(VerdictKind.Rejected, "", attempts, tNeeded, minEffect, values.Length, events, power, sd, t, values.Average(), first, second);
        if (t < tNeeded)
        {
            return result with { Reason = string.Create(CultureInfo.InvariantCulture, $"t {t:F2} < {tNeeded:F2}") };
        }

        if (!(first > 0 && second > 0))
        {
            return result with { Reason = string.Create(CultureInfo.InvariantCulture, $"sign differs between halves ({first:F4}, {second:F4})") };
        }

        return result with { Kind = VerdictKind.Passed, Reason = string.Create(CultureInfo.InvariantCulture, $"t {t:F2} >= {tNeeded:F2}, both halves positive") };
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        double mean = values.Average();
        double sum = values.Sum(value => (value - mean) * (value - mean));
        return Math.Sqrt(sum / (values.Count - 1));
    }
}
