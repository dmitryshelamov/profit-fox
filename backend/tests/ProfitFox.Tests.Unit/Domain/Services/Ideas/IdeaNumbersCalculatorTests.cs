using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Ожидание идеи", "Значок идеи", "Вероятность идеи" and "Ожидание идеи в деньгах" (decision 5 of add-own-idea-numbers): the own
///     numbers of an idea from its plan, the fees of its category, its bucket over 90 days and the curve of its set, by the fees of linear.
/// </summary>
public class IdeaNumbersCalculatorTests
{
    private static readonly FeeRates Linear = new FeeRates(0.02m, 0.055m, 0.05m);

    // The curve of «Уровни» 1 h long linear of scenario "Множители полос": 0.675 from 3 to 6, 1.375 below 2.
    private static readonly RiskRewardCurve Curve = new RiskRewardCurve(
        Interval.OneHour,
        IdeaSide.Long,
        MarketCategory.Linear,
        [
            new RiskRewardBandCount(RiskRewardBand.UpTo2, 400, 110),
            new RiskRewardBandCount(RiskRewardBand.From2To3, 300, 57),
            new RiskRewardBandCount(RiskRewardBand.From3To6, 200, 27),
            new RiskRewardBandCount(RiskRewardBand.From6, 100, 6)
        ]);

    // Scenario "Две идеи одной корзины": costs of 0.70 R and 0.042 R, the interval of the bucket shifted by them.
    [TestCase(99.7, -0.72, -0.76, -0.68)]
    [TestCase(95, -0.062, -0.102, -0.022)]
    public void Expectancy_of_an_idea_is_that_of_its_bucket_without_costs_less_its_costs(decimal stop, decimal expectancy, decimal low, decimal high)
    {
        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, stop), Linear, Model(-0.02m, -0.06m, 0.02m), null);

        (numbers.Expectancy, numbers.ExpectancyLow, numbers.ExpectancyHigh).ShouldBe((expectancy, low, high));
    }

    // Scenario "Мало данных": the own mean of +0.40 R less the costs of 0.105 R, without an interval, a badge or a probability.
    [Test]
    public void Idea_of_a_bucket_of_too_little_data_has_an_expectancy_without_an_interval()
    {
        var bucket = new BucketStatistics(12, 6, 6, 0, null, null, null, 0.3m, null, null) { GrossExpectancy = 0.4m };

        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, 98m), Linear, bucket, null);

        (numbers.Expectancy, numbers.ExpectancyLow, numbers.ExpectancyHigh, numbers.Badge, numbers.Probability).ShouldBe((0.295m, null, null, null, null));
    }

    // Scenario "Идея без корзины".
    [Test]
    public void Idea_without_a_bucket_has_no_numbers_but_its_costs()
    {
        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, 98m), Linear, null, null);

        (numbers.Expectancy, numbers.Probability, numbers.ExpectancyPerUsdt, numbers.Costs).ShouldBe((null, null, null, 0.105m));
    }

    // Scenario "Корзина до первого пересчёта": the bucket was written before its expectancy without costs was counted.
    [Test]
    public void Idea_of_a_bucket_without_the_expectancy_without_costs_has_no_numbers()
    {
        BucketStatistics bucket = Model(-0.02m, -0.06m, 0.02m) with { GrossExpectancy = null, GrossExpectancyLow = null, GrossExpectancyHigh = null };

        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, 98m), Linear, bucket, null);

        (numbers.Expectancy, numbers.ExpectancyLow, numbers.Probability, numbers.Badge).ShouldBe((null, null, null, null));
    }

    // Scenario "Разные значки в одной корзине": +0.10 R with +0.06…+0.14 R and costs of 0.042, 0.105 and 0.70 R.
    [TestCase(95, 0.058, IdeaBadge.Up)]
    [TestCase(98, -0.005, IdeaBadge.Flat)]
    [TestCase(99.7, -0.6, IdeaBadge.Down)]
    public void Badge_of_an_idea_goes_by_the_interval_of_its_expectancy(decimal stop, decimal expectancy, IdeaBadge badge)
    {
        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, stop), Linear, Model(0.1m, 0.06m, 0.14m), null);

        (numbers.Expectancy, numbers.Badge).ShouldBe((expectancy, badge));
    }

    // Scenarios "Дальняя цель «Уровней»" and "Ближняя цель «Уровней»": the probability of the bucket and its interval times the multiplier of
    // the band of the reward to risk of the idea, 4 and 1.8.
    [TestCase(108, 0.675, 0.1485, 0.1215, 0.1755)]
    [TestCase(103.6, 1.375, 0.3025, 0.2475, 0.3575)]
    public void Probability_of_levels_is_that_of_its_bucket_times_the_multiplier_of_its_band(decimal target, decimal multiplier, decimal probability, decimal low, decimal high)
    {
        BucketStatistics bucket = Model(-0.02m, -0.06m, 0.02m) with { Probability = 0.22m, ProbabilityLow = 0.18m, ProbabilityHigh = 0.26m };

        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.Levels, 98m, target), Linear, bucket, Curve);

        (numbers.Multiplier, numbers.Probability, numbers.ProbabilityLow, numbers.ProbabilityHigh).ShouldBe((multiplier, probability, low, high));
    }

    // The band of the reward to risk the multiplier of «Уровни» is of, which the hint of the probability names; none for another type.
    [Test]
    public void Numbers_of_levels_name_the_band_of_their_multiplier()
    {
        IdeaNumbersCalculator.Compute(Plan(IdeaType.Levels, 98m, 108m), Linear, null, Curve).Band.ShouldBe(RiskRewardBand.From3To6);
        IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, 98m, 108m), Linear, null, Curve).Band.ShouldBeNull();
    }

    // Scenario "Не больше единицы".
    [Test]
    public void Probability_of_levels_is_never_above_one()
    {
        BucketStatistics bucket = Model(-0.02m, -0.06m, 0.02m) with { Probability = 0.8m, ProbabilityLow = 0.75m, ProbabilityHigh = 0.85m };

        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.Levels, 98m, 103.6m), Linear, bucket, Curve);

        (numbers.Probability, numbers.ProbabilityLow, numbers.ProbabilityHigh).ShouldBe((1m, 1m, 1m));
    }

    // Scenario "Другой тип": the curve of «Уровни» does not touch the probability of another type.
    [Test]
    public void Probability_of_another_type_is_that_of_its_bucket()
    {
        BucketStatistics bucket = Model(-0.02m, -0.06m, 0.02m) with { Probability = 0.31m, ProbabilityLow = 0.27m, ProbabilityHigh = 0.35m };

        IdeaNumbers numbers = IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, 98m, 108m), Linear, bucket, Curve);

        (numbers.Multiplier, numbers.Probability, numbers.ProbabilityLow, numbers.ProbabilityHigh).ShouldBe((1m, 0.31m, 0.27m, 0.35m));
    }

    // Scenario "Мало своих данных": a bucket of 12 cases of its own has the probability of the model by its parent.
    [Test]
    public void Probability_of_a_bucket_of_few_own_cases_is_that_of_the_model()
    {
        BucketStatistics bucket = Model(-0.02m, -0.06m, 0.02m) with { Probability = 0.28m, ProbabilityLow = 0.24m, ProbabilityHigh = 0.32m, FewOwnCases = true };

        IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, 98m), Linear, bucket, null).Probability.ShouldBe(0.28m);
    }

    // Scenario "Узкий стоп теряет меньше в деньгах": the expectancy in R times the risk on 1 USDT of the amount, 0.003 and 0.05.
    [TestCase(99.7, -0.00216)]
    [TestCase(95, -0.0031)]
    public void Expectancy_per_usdt_is_the_expectancy_in_r_times_the_risk_per_usdt(decimal stop, decimal perUsdt)
    {
        IdeaNumbersCalculator.Compute(Plan(IdeaType.TwoR, stop), Linear, Model(-0.02m, -0.06m, 0.02m), null).ExpectancyPerUsdt.ShouldBe(perUsdt);
    }

    private static IdeaPlan Plan(IdeaType type, decimal stop, decimal target = 104m)
    {
        return new IdeaPlan(type, IdeaSide.Long, 100m, stop, target);
    }

    // A bucket of the model with the expectancy −0.21 R after costs and the given one without them.
    private static BucketStatistics Model(decimal gross, decimal grossLow, decimal grossHigh)
    {
        return new BucketStatistics(400, 180, 200, 20, 0.45m, 0.4m, 0.5m, -0.21m, -0.25m, -0.17m)
        {
            GrossExpectancy = gross,
            GrossExpectancyLow = grossLow,
            GrossExpectancyHigh = grossHigh
        };
    }
}