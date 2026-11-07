using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Decision 2 and requirement "Подтягивание к родительской корзине": the numbers of a bucket shrunk to those of its parent.</summary>
public class BucketModelTests
{
    private const decimal Precision = 0.001m;

    // τ = 0.1 R between the buckets of the parent.
    private const double Tau = 0.01;

    private static readonly DateTimeOffset Hour = DateTimeOffset.Parse("2024-06-01T10:00:00Z");
    private static readonly IdeaBucketKey Impulse = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
    private static readonly IdeaBucketKey Drop = Impulse with { Setup = IdeaSetup.Drop };

    // The parent: 9 000 cases with the mean −0.20 R and s 1.2 R, the variance of its mean s² / n.
    private static readonly BucketModel.Estimate Parent = new BucketModel.Estimate(-0.2m, 1.44 / 9000);

    // The same parent with 40 % of targets and its root at itself.
    private static readonly BucketModel.Level NineThousand = new BucketModel.Level(9000, new BucketModel.Estimate(0.4m, 0.24 / 9000), Parent);

    // Scenario "Маленькая корзина с удачей": B = 0.01 / (0.01 + 1.44 / 35) = 0.196.
    [Test]
    public void Small_lucky_bucket_is_pulled_to_its_parent()
    {
        (BucketModel.Estimate model, double parentWeight) = BucketModel.Shrink(new BucketModel.Estimate(0.3m, 1.44 / 35), Parent, Tau);

        model.Mean.ShouldBe(-0.102m, Precision);
        model.Low.ShouldBe(-0.279m, Precision);
        model.High.ShouldBe(0.075m, Precision);
        parentWeight.ShouldBe(0.804, 0.001);
    }

    // Scenario "Большая корзина почти не сдвигается": B = 0.01 / (0.01 + 1.44 / 5 000) = 0.972.
    [Test]
    public void Large_bucket_hardly_moves()
    {
        (BucketModel.Estimate model, _) = BucketModel.Shrink(new BucketModel.Estimate(0.1m, 1.44 / 5000), Parent, Tau);

        model.Mean.ShouldBe(0.092m, Precision);
        model.Low.ShouldBe(0.059m, Precision);
        model.High.ShouldBe(0.124m, Precision);
    }

    // DerSimonian–Laird: means 0 and 1 of variance 0.1, w = 10 each, Q = 5, Σw − Σw² / Σw = 10: τ² = (5 − 1) / 10.
    [Test]
    public void Spread_between_the_buckets_is_estimated_from_how_they_differ()
    {
        BucketModel.Spread([new BucketModel.Estimate(0m, 0.1), new BucketModel.Estimate(1m, 0.1)]).ShouldBe(0.4, 0.000001);
    }

    // Means 0 and 0.1 differ less than chance would make them: Q = 0.05 below k − 1, τ² = 0.
    [Test]
    public void Buckets_that_differ_by_chance_have_no_spread()
    {
        BucketModel.Spread([new BucketModel.Estimate(0m, 0.1), new BucketModel.Estimate(0.1m, 0.1)]).ShouldBe(0);
    }

    // Decision 3 of add-idea-features, DerSimonian–Laird pooled over groups around the weighted mean of each: the groups of means 0 and 1 and of
    // 0 and 0.2, variance 0.1, give Q = 5 + 0.2 over 2 degrees of freedom and Σw − Σw² / Σw = 10 + 10: τ² = (5.2 − 2) / 20; groups of one
    // member and of none add nothing.
    [Test]
    public void Spread_pooled_over_groups_is_estimated_from_how_the_members_differ_within_each()
    {
        BucketModel.PooledSpread(
        [
            [new BucketModel.Estimate(0m, 0.1), new BucketModel.Estimate(1m, 0.1)],
            [new BucketModel.Estimate(0m, 0.1), new BucketModel.Estimate(0.2m, 0.1)],
            [new BucketModel.Estimate(5m, 0.1)],
            []
        ]).ShouldBe(0.16, 0.000001);
    }

    // Decision 3 of add-idea-features: one bucket of a parent, 30 +2 R targets and 30 −1 R stops sharing their hours, the two in each hour
    // making the clustered variance no wider than that of independent cases: 0.25 / 60 of the share and s² / 60 = 135 / 59 / 60 of the mean.
    // Its two levels have no spread of their own and take that of the bucket over their 30 cases each, twice as much: τ² = 59 / 120 of the share
    // and (59 − 1) / 13.11 = 4.4237 of the mean; a level of fewer than 30 cases and a bucket without a case add nothing.
    [Test]
    public void Spread_of_the_features_is_that_of_the_levels_around_their_bucket_over_the_buckets_of_a_parent()
    {
        ClusteredSums targets = Equal(30, IdeaStatus.Target, 2m);
        ClusteredSums stops = Equal(30, IdeaStatus.Stop, -1m);
        ClusteredSums bucket = Equal(30, IdeaStatus.Target, 2m);

        foreach (int hour in Enumerable.Range(0, 30))
        {
            bucket.Add(Hour.AddHours(hour), IdeaStatus.Stop, -1m);
        }

        BucketModel.Spreads spreads = BucketModel.FeatureSpreads([(bucket, [targets, stops, Equal(5, IdeaStatus.Expired, 0m)]), (new ClusteredSums(TimeSpan.FromHours(1)), [])], new BucketModel.ParentModel(NineThousand, NineThousand));

        spreads.Share.ShouldBe(59.0 / 120, 0.00001);
        spreads.Expectancy.ShouldBe(4.42373, 0.00001);
    }

    [Test]
    public void One_bucket_has_no_spread()
    {
        BucketModel.Spread([new BucketModel.Estimate(1m, 0.1)]).ShouldBe(0);
    }

    // Scenario "Корзины родителя не различаются": two buckets of the same results, the only parent of the category, which is the root.
    [Test]
    public void Buckets_of_a_parent_that_do_not_differ_have_the_numbers_of_the_parent()
    {
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Cases(0), [Drop] = Cases(100) };
        ClusteredSums parent = Cases(0, 100);

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = Build(buckets, parent, Cases(0, 100));

        BucketStatistics own = parent.Statistics();
        model[Impulse].ShouldBe(model[Drop], "the buckets differ in no number but their own ones");
        (model[Impulse].Probability!.Value, model[Impulse].Expectancy).ShouldBe((own.Probability!.Value, own.Expectancy));
        model[Impulse].ExpectancyLow!.Value.ShouldBe(own.ExpectancyLow!.Value, 0.0000001m);
        model[Impulse].ExpectancyHigh!.Value.ShouldBe(own.ExpectancyHigh!.Value, 0.0000001m);
        model[Impulse].ParentWeight.ShouldBe(1m);
        BucketStatistics bucket = Cases(0).Statistics();
        (model[Impulse].OwnProbability, model[Impulse].OwnProbabilityLow, model[Impulse].OwnProbabilityHigh)
            .ShouldBe((bucket.Probability, bucket.ProbabilityLow, bucket.ProbabilityHigh));
        (model[Impulse].OwnExpectancy, model[Impulse].OwnExpectancyLow, model[Impulse].OwnExpectancyHigh)
            .ShouldBe((bucket.Expectancy, bucket.ExpectancyLow, bucket.ExpectancyHigh));
    }

    // Scenario "12 своих случаев": the spread of the bucket is that of its parent over its 12 cases, 1.44 / 12; B = 0.01 / 0.13.
    [Test]
    public void Bucket_of_few_own_cases_takes_its_spread_from_the_parent()
    {
        BucketStatistics statistics = BucketModel.Statistics(Equal(12, IdeaStatus.Target, 0.5m), new BucketModel.ParentModel(NineThousand, NineThousand), new BucketModel.Spreads(0, Tau));

        statistics.Expectancy.ShouldBe(-0.146m, Precision);
        statistics.ExpectancyLow!.Value.ShouldBe(-0.336m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(0.044m, Precision);
        (statistics.FewOwnCases, statistics.Badge).ShouldBe((true, IdeaBadge.Flat));
    }

    // Scenario "Корзина как родитель подкорзин" of add-idea-features: the bucket of "12 своих случаев" is the parent of its feature sub-buckets
    // with the numbers of its model. Scenario "Тонкий уровень остаётся с корзиной": a sub-bucket of the same 12 cases, among sub-buckets that do
    // not differ beyond chance, has the numbers of that model; "Подкорзины различаются" is the test above, the bucket taking the sub-bucket's place.
    [Test]
    public void Bucket_is_the_parent_of_its_feature_sub_buckets_with_the_numbers_of_its_model()
    {
        BucketModel.ParentModel bucket = BucketModel.Bucket(Equal(12, IdeaStatus.Target, 0.5m), new BucketModel.ParentModel(NineThousand, NineThousand), new BucketModel.Spreads(0, Tau));

        bucket.Own.Cases.ShouldBe(12);
        (bucket.Model.Cases, bucket.Model.Share.Mean).ShouldBe((12, 0.4m));
        bucket.Model.Expectancy.Mean.ShouldBe(-0.146m, Precision);
        bucket.Model.Expectancy.Low.ShouldBe(-0.336m, Precision);
        bucket.Model.Expectancy.High.ShouldBe(0.044m, Precision);
        BucketStatistics level = BucketModel.Statistics(Equal(12, IdeaStatus.Target, 0.5m), bucket, default);
        (level.Expectancy, level.ExpectancyLow, level.ExpectancyHigh).ShouldBe((bucket.Model.Expectancy.Mean, bucket.Model.Expectancy.Low, bucket.Model.Expectancy.High));
        (level.Probability, level.ParentWeight).ShouldBe((0.4m, 1m));
    }

    // A share of 0 and equal results have no spread of their own: the bucket takes the spread of its parent over its 30 cases, 0.24 / 30 and
    // 1.44 / 30, and is not taken for certain.
    [Test]
    public void Bucket_without_a_spread_of_its_own_takes_the_spread_of_the_parent()
    {
        BucketStatistics statistics = BucketModel.Statistics(Equal(30, IdeaStatus.Stop, -1m), new BucketModel.ParentModel(NineThousand, NineThousand), new BucketModel.Spreads(Tau, Tau));

        // B = 0.01 / 0.018 of the share and 0.01 / 0.058 of the expectancy.
        statistics.Probability!.Value.ShouldBe(0.1778m, 0.0001m);
        statistics.Expectancy.ShouldBe(-0.3379m, 0.0001m);
        statistics.FewOwnCases.ShouldBeFalse();
    }

    // Scenario "Мало данных у родителя": 12 and 13 cases in a parent of 25.
    [Test]
    public void Bucket_of_a_parent_of_fewer_than_30_cases_has_too_little_data()
    {
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Equal(12, IdeaStatus.Target, 0.5m), [Drop] = Equal(13, IdeaStatus.Stop, -1m) };

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = Build(buckets, Equal(12, IdeaStatus.Target, 0.5m, 13), Equal(12, IdeaStatus.Target, 0.5m, 13));

        model[Impulse].ShouldBe(new BucketStatistics(12, 12, 0, 0, null, null, null, 0.5m, null, null) { OwnExpectancy = 0.5m });
        (model[Impulse].Badge, model[Impulse].FewOwnCases).ShouldBe((null, false));
    }

    // A category of one case has no spread at all: its bucket has too little data.
    [Test]
    public void Lone_case_has_too_little_data()
    {
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Equal(1, IdeaStatus.Target, 2m) };

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = Build(buckets, Equal(1, IdeaStatus.Target, 2m), Equal(1, IdeaStatus.Target, 2m));

        model[Impulse].Probability.ShouldBeNull();
    }

    // The spread between the buckets is that of the buckets of 30 cases and more: a small one far from the rest does not make it.
    [Test]
    public void Buckets_of_fewer_than_30_cases_do_not_make_the_spread()
    {
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Cases(100), [Drop] = Equal(12, IdeaStatus.Target, 2m) };

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = Build(buckets, WithTargets(Cases(100), 12), WithTargets(Cases(100), 12));

        model[Impulse].ParentWeight.ShouldBe(1m);
    }

    // A pass of a type × interval takes the roots of the last whole pass, which may not know a category.
    [Test]
    public void Bucket_whose_category_has_no_root_has_too_little_data()
    {
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Cases(0) };
        Dictionary<BucketModel.Parent, ClusteredSums> parents = new() { [BucketModel.Parent.Of(Impulse)] = Cases(0) };

        BucketModel.Build(buckets, parents, new Dictionary<MarketCategory, BucketModel.Root>())[Impulse].Probability.ShouldBeNull();
    }

    // Decision 2 on the share: the long parent of 40 targets of 60 is pulled to its category of 91 of 180, and its only bucket has its numbers;
    // the short bucket of 1 target of 60 next to one of 50 is hardly pulled, and its interval stops at 0.
    [Test]
    public void Share_of_the_model_is_pulled_to_the_parent_and_the_parent_to_the_category()
    {
        IdeaBucketKey shortImpulse = Impulse with { Side = IdeaSide.Short };
        IdeaBucketKey shortDrop = Drop with { Side = IdeaSide.Short };
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Targets(0, 40), [shortImpulse] = Targets(100, 1), [shortDrop] = Targets(200, 50) };
        Dictionary<BucketModel.Parent, ClusteredSums> parents = new()
        {
            [BucketModel.Parent.Of(Impulse)] = Targets(0, 40),
            [BucketModel.Parent.Of(shortImpulse)] = Targets(200, 50, Targets(100, 1))
        };
        ClusteredSums category = Targets(200, 50, Targets(100, 1, Targets(0, 40)));

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = BucketModel.Build(buckets, parents, BucketModel.Roots(parents, new Dictionary<MarketCategory, ClusteredSums> { [MarketCategory.Linear] = category }));

        model[Impulse].Probability!.Value.ShouldBe(0.6457m, 0.0001m);
        model[Impulse].Probability!.Value.ShouldBeInRange(91m / 180m, 40m / 60m);
        // The mean +1 R of the long parent pulled to +0.517 R of the category.
        model[Impulse].Expectancy.ShouldBe(0.9370m, 0.0001m);
        model[shortImpulse].Probability!.Value.ShouldBe(0.0170m, 0.0001m);
        model[shortImpulse].ProbabilityLow.ShouldBe(0m);
        model[shortImpulse].ProbabilityHigh!.Value.ShouldBe(0.0504m, 0.0001m);
    }

    // The mirror of the short bucket: 59 targets of 60 next to 10, the interval stops at 1.
    [Test]
    public void Share_of_the_model_stops_at_1()
    {
        IdeaBucketKey shortDrop = Drop with { Side = IdeaSide.Short };
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Targets(100, 59), [Drop] = Targets(200, 10), [shortDrop] = Targets(0, 20) };
        Dictionary<BucketModel.Parent, ClusteredSums> parents = new()
        {
            [BucketModel.Parent.Of(Impulse)] = Targets(200, 10, Targets(100, 59)),
            [BucketModel.Parent.Of(shortDrop)] = Targets(0, 20)
        };
        ClusteredSums category = Targets(200, 10, Targets(100, 59, Targets(0, 20)));

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = BucketModel.Build(buckets, parents, BucketModel.Roots(parents, new Dictionary<MarketCategory, ClusteredSums> { [MarketCategory.Linear] = category }));

        model[Impulse].ProbabilityHigh.ShouldBe(1m);
    }

    // Decision 5: a case of the calibration may find no case of its bucket closed before it, while its parent has them: it is promised the
    // numbers of the model of the parent.
    [Test]
    public void Bucket_without_a_case_of_its_own_has_the_numbers_of_its_parent()
    {
        BucketStatistics statistics = BucketModel.Statistics(new ClusteredSums(TimeSpan.FromHours(1)), new BucketModel.ParentModel(NineThousand, NineThousand), new BucketModel.Spreads(Tau, Tau));

        (statistics.Cases, statistics.Probability, statistics.Expectancy, statistics.ExpectancyLow, statistics.ExpectancyHigh)
            .ShouldBe((0, 0.4m, -0.2m, Parent.Low, Parent.High));
        (statistics.FewOwnCases, statistics.ParentWeight).ShouldBe((true, 1m));
    }

    // A category of 30 cases and more without a target, or whose results are all equal, has no spread to shrink by: its buckets have too
    // little data.
    [TestCase(IdeaStatus.Stop, -1, -0.5)]
    [TestCase(IdeaStatus.Target, 0.5, 0.5)]
    public void Category_without_a_spread_has_too_little_data(IdeaStatus first, decimal firstResult, decimal restResult)
    {
        Dictionary<IdeaBucketKey, ClusteredSums> buckets = new() { [Impulse] = Mixed() };

        IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> model = Build(buckets, Mixed(), Mixed());

        model[Impulse].Probability.ShouldBeNull();

        // 15 cases of the first outcome, then 15 stops.
        ClusteredSums Mixed()
        {
            var sums = new ClusteredSums(TimeSpan.FromHours(1));

            for (int index = 0; index < 30; index++)
            {
                sums.Add(Hour.AddHours(index), index < 15 ? first : IdeaStatus.Stop, index < 15 ? firstResult : restResult);
            }

            return sums;
        }
    }

    private static IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> Build(
        Dictionary<IdeaBucketKey, ClusteredSums> buckets,
        ClusteredSums parent,
        ClusteredSums category)
    {
        Dictionary<BucketModel.Parent, ClusteredSums> parents = new() { [BucketModel.Parent.Of(Impulse)] = parent };

        return BucketModel.Build(buckets, parents, BucketModel.Roots(parents, new Dictionary<MarketCategory, ClusteredSums> { [MarketCategory.Linear] = category }));
    }

    // 30 cases each in its own hour from each of the given ones: a third are +2 R targets, the rest −1 R stops.
    private static ClusteredSums Cases(params int[] fromHours)
    {
        var sums = new ClusteredSums(TimeSpan.FromHours(1));

        foreach (int fromHour in fromHours)
        {
            for (int index = 0; index < 30; index++)
            {
                sums.Add(Hour.AddHours(fromHour + index), index % 3 == 0 ? IdeaStatus.Target : IdeaStatus.Stop, index % 3 == 0 ? 2m : -1m);
            }
        }

        return sums;
    }

    // Cases of the same outcome and result, each in its own hour, and after them as many of the next as asked for, stops of −1 R.
    private static ClusteredSums Equal(int count, IdeaStatus status, decimal result, int stops = 0)
    {
        var sums = new ClusteredSums(TimeSpan.FromHours(1));

        for (int index = 0; index < count; index++)
        {
            sums.Add(Hour.AddHours(index), status, result);
        }

        for (int index = 0; index < stops; index++)
        {
            sums.Add(Hour.AddHours(count + index), IdeaStatus.Stop, -1m);
        }

        return sums;
    }

    // The sums with as many +2 R targets more, each in its own hour from the first.
    private static ClusteredSums WithTargets(ClusteredSums sums, int count)
    {
        for (int index = 0; index < count; index++)
        {
            sums.Add(Hour.AddHours(index), IdeaStatus.Target, 2m);
        }

        return sums;
    }

    // 60 cases each in its own hour from the given one into the sums, the first ones +2 R targets, the rest −1 R stops.
    private static ClusteredSums Targets(int fromHour, int targets, ClusteredSums? sums = null)
    {
        sums ??= new ClusteredSums(TimeSpan.FromHours(1));

        for (int index = 0; index < 60; index++)
        {
            sums.Add(Hour.AddHours(fromHour + index), index < targets ? IdeaStatus.Target : IdeaStatus.Stop, index < targets ? 2m : -1m);
        }

        return sums;
    }
}