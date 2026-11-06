using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The model of the buckets (decision 2 of honest-idea-intervals): the numbers of a bucket shrunk to the model of its parent
///     «тип × ТФ × сторона × категория», those of the parent to its category, the root, by a normal model whose spread between the members
///     of a level, τ², is estimated by DerSimonian–Laird from how they differ. Variances and weights are statistics, not money: they go
///     through double, the means stay decimal.
/// </summary>
public static class BucketModel
{
    // The 97.5th percentile of the standard normal distribution: a 95 % interval.
    private const double Z = 1.96;

    /// <summary>
    ///     The numbers of the buckets that have a case, each shrunk to the model of its parent; the parents shrunk to the
    ///     <paramref name="roots" /> of their categories.
    /// </summary>
    public static IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> Build(
        IReadOnlyDictionary<IdeaBucketKey, ClusteredSums> buckets,
        IReadOnlyDictionary<Parent, ClusteredSums> parents,
        IReadOnlyDictionary<MarketCategory, Root> roots)
    {
        var models = new Dictionary<Parent, (ParentModel Model, Spreads Spreads)>();

        foreach ((Parent parent, ClusteredSums sums) in parents)
        {
            if (Model(sums, roots.GetValueOrDefault(parent.Category)) is { } model)
            {
                models[parent] = (model, Between(buckets.Where(bucket => Parent.Of(bucket.Key) == parent).Select(bucket => bucket.Value), model.Own));
            }
        }

        return buckets.ToDictionary(
            bucket => bucket.Key,
            bucket => models.TryGetValue(Parent.Of(bucket.Key), out (ParentModel Model, Spreads Spreads) parent)
                ? Statistics(bucket.Value, parent.Model, parent.Spreads)
                : TooLittleData(bucket.Value));
    }

    /// <summary>The roots of the categories that have one: the own numbers of each and the spread between its parents.</summary>
    public static IReadOnlyDictionary<MarketCategory, Root> Roots(IReadOnlyDictionary<Parent, ClusteredSums> parents, IReadOnlyDictionary<MarketCategory, ClusteredSums> categories)
    {
        var roots = new Dictionary<MarketCategory, Root>();

        foreach ((MarketCategory category, ClusteredSums sums) in categories)
        {
            if (Category(sums) is { } level)
            {
                roots[category] = new Root(level, Between(parents.Where(parent => parent.Key.Category == category).Select(parent => parent.Value), level));
            }
        }

        return roots;
    }

    /// <summary>The own numbers of a category as a root: none under 30 cases, nor without a spread of either number, which nothing shrinks by.</summary>
    public static Level? Category(ClusteredSums sums)
    {
        return sums.Cases < BucketStatisticsCalculator.MinimumCases || sums.ShareVariance == 0 || sums.ExpectancyVariance == 0 ? null : Level.Root(sums);
    }

    /// <summary>
    ///     The own numbers of a parent and its model, shrunk to its <paramref name="root" />; none under 30 cases or without a root: its buckets have
    ///     too little data.
    /// </summary>
    public static ParentModel? Model(ClusteredSums sums, Root? root)
    {
        if (sums.Cases < BucketStatisticsCalculator.MinimumCases || root is null)
        {
            return null;
        }

        var own = Level.Of(sums, root.Category);

        return new ParentModel(
            own,
            new Level(own.Cases, Shrink(own.Share, root.Category.Share, root.Spreads.Share).Model, Shrink(own.Expectancy, root.Category.Expectancy, root.Spreads.Expectancy).Model));
    }

    /// <summary>The spread between the members of a <paramref name="level" />, of those of 30 cases and more.</summary>
    public static Spreads Between(IEnumerable<ClusteredSums> members, Level level)
    {
        Level[] counted = [.. members.Where(member => member.Cases >= BucketStatisticsCalculator.MinimumCases).Select(member => Level.Of(member, level))];

        return new Spreads(Spread([.. counted.Select(member => member.Share)]), Spread([.. counted.Select(member => member.Expectancy)]));
    }

    /// <summary>
    ///     A number shrunk to that of the level above: B = τ² / (τ² + v), the mean μ + B·(m − μ) with the variance B·v + (1 − B)²·Var(μ), and the
    ///     weight of the level above in it, 1 − B.
    /// </summary>
    public static (Estimate Model, double ParentWeight) Shrink(Estimate own, Estimate parent, double spread)
    {
        double weight = spread / (spread + own.Variance);
        decimal mean = parent.Mean + ((decimal)weight * (own.Mean - parent.Mean));

        return (new Estimate(mean, (weight * own.Variance) + ((1 - weight) * (1 - weight) * parent.Variance)), 1 - weight);
    }

    /// <summary>
    ///     τ², the spread between the members of a level beyond chance, by DerSimonian–Laird: w = 1 / v, Q = Σw(m − m̄_w)²,
    ///     τ² = max(0, (Q − (k − 1)) / (Σw − Σw² / Σw)); 0 with fewer than two members.
    /// </summary>
    public static double Spread(IReadOnlyList<Estimate> members)
    {
        if (members.Count < 2)
        {
            return 0;
        }

        double[] weights = [.. members.Select(member => 1 / member.Variance)];
        double total = weights.Sum();
        double mean = members.Select((member, index) => weights[index] * (double)member.Mean).Sum() / total;
        double q = members.Select((member, index) => weights[index] * Math.Pow((double)member.Mean - mean, 2)).Sum();

        return Math.Max(0, (q - (members.Count - 1)) / (total - (weights.Sum(weight => weight * weight) / total)));
    }

    /// <summary>
    ///     The numbers of a bucket shrunk to the model of its <paramref name="parent" /> by the spread between the buckets of the parent; a bucket
    ///     without a case of its own has the numbers of the model of the parent.
    /// </summary>
    public static BucketStatistics Statistics(ClusteredSums sums, ParentModel parent, Spreads spreads)
    {
        if (sums.Cases == 0)
        {
            return Numbers(sums, parent.Model.Share, parent.Model.Expectancy) with { ParentWeight = 1m, FewOwnCases = true };
        }

        var level = Level.Of(sums, parent.Own);
        BucketStatistics own = sums.Statistics();
        (Estimate share, _) = Shrink(level.Share, parent.Model.Share, spreads.Share);
        (Estimate expectancy, double parentWeight) = Shrink(level.Expectancy, parent.Model.Expectancy, spreads.Expectancy);

        return Numbers(sums, share, expectancy) with
        {
            OwnProbability = own.Probability,
            OwnProbabilityLow = own.ProbabilityLow,
            OwnProbabilityHigh = own.ProbabilityHigh,
            OwnExpectancy = own.Expectancy,
            OwnExpectancyLow = own.ExpectancyLow,
            OwnExpectancyHigh = own.ExpectancyHigh,
            ParentWeight = (decimal)parentWeight,
            FewOwnCases = sums.Cases < BucketStatisticsCalculator.MinimumCases
        };
    }

    // The counts of the bucket with the numbers of the model, the interval of the share within [0, 1].
    private static BucketStatistics Numbers(ClusteredSums sums, Estimate share, Estimate expectancy)
    {
        return new BucketStatistics(sums.Cases, sums.Targets, sums.Stops, sums.Expired, share.Mean, Math.Max(0m, share.Low), Math.Min(1m, share.High), expectancy.Mean,
            expectancy.Low, expectancy.High);
    }

    // «Мало данных»: the counts and the own expectancy only.
    private static BucketStatistics TooLittleData(ClusteredSums sums)
    {
        decimal expectancy = sums.TotalR / sums.Cases;

        return new BucketStatistics(sums.Cases, sums.Targets, sums.Stops, sums.Expired, null, null, null, expectancy, null, null) { OwnExpectancy = expectancy };
    }

    /// <summary>A number of a level, a share of targets or a mean result, with the variance of it and its 95 % interval.</summary>
    public readonly record struct Estimate(decimal Mean, double Variance)
    {
        public decimal Low => Mean - Margin;

        public decimal High => Mean + Margin;

        private decimal Margin => (decimal)(Z * Math.Sqrt(Variance));
    }

    /// <summary>The cases of a level and its share of targets and mean result.</summary>
    public sealed record Level(int Cases, Estimate Share, Estimate Expectancy)
    {
        /// <summary>
        ///     The own numbers of a level. One of fewer than 30 cases, or without a spread of its own (a share of 0 or 1, equal results), takes the
        ///     spread of the level <paramref name="above" /> over its cases: v = V_above · n_above / n.
        /// </summary>
        public static Level Of(ClusteredSums sums, Level above)
        {
            bool enough = sums.Cases >= BucketStatisticsCalculator.MinimumCases;

            return new Level(
                sums.Cases,
                new Estimate((decimal)sums.Targets / sums.Cases, Variance(enough ? sums.ShareVariance : 0, above.Share, above.Cases, sums.Cases)),
                new Estimate(sums.TotalR / sums.Cases, Variance(enough ? sums.ExpectancyVariance : 0, above.Expectancy, above.Cases, sums.Cases)));
        }

        /// <summary>The own numbers of a root, a level of 30 cases and more with nothing above it.</summary>
        public static Level Root(ClusteredSums sums)
        {
            return new Level(sums.Cases, new Estimate((decimal)sums.Targets / sums.Cases, sums.ShareVariance), new Estimate(sums.TotalR / sums.Cases, sums.ExpectancyVariance));
        }

        private static double Variance(double own, Estimate above, int aboveCases, int cases)
        {
            return own > 0 ? own : above.Variance * aboveCases / cases;
        }
    }

    /// <summary>τ² of the share and of the mean result between the members of a level.</summary>
    public readonly record struct Spreads(double Share, double Expectancy);

    /// <summary>The own numbers of a parent and those of its model.</summary>
    public sealed record ParentModel(Level Own, Level Model);

    /// <summary>The root a parent is shrunk to: the own numbers of its category and the spread between the parents of the category.</summary>
    public sealed record Root(Level Category, Spreads Spreads);

    /// <summary>The parent of a bucket: its type × interval × side × category, over all setups.</summary>
    public readonly record struct Parent(IdeaType Type, Interval Interval, IdeaSide Side, MarketCategory Category)
    {
        public static Parent Of(IdeaBucketKey key)
        {
            return new Parent(key.Type, key.Interval, key.Side, key.Category);
        }
    }
}