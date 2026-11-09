using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.SavedFilters;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.SavedFilters;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.SavedFilters;

/// <summary>Requirement "Сохранённые фильтры": the server applies a filter to the pairs and the ideas.</summary>
public class SavedFilterMatchTests
{
    private static readonly PairMetrics None = MarketSnapshotRowGenerator.NoMetrics;
    private static readonly PairSetup[] NoSetups = [];

    [TestCase(">", 5, null, "5.01", true)]
    [TestCase(">", 5, null, "5", false)]
    [TestCase(">=", 5, null, "5.0", true)]
    [TestCase("<", 5, null, "4.99", true)]
    [TestCase("<", 5, null, "5", false)]
    [TestCase("<=", 5, null, "5", true)]
    [TestCase("=", 5, null, "5.00", true)]
    [TestCase("=", 5, null, "5.01", false)]
    [TestCase("between", 1, 3, "1", true)]
    [TestCase("between", 1, 3, "3", true)]
    [TestCase("between", 1, 3, "3.01", false)]
    [TestCase("between", 1, 3, "0.99", false)]
    public void A_pair_is_tested_by_each_operator(string op, int value, int? to, string actual, bool expected)
    {
        FilterCondition condition = Compare(FilterField.Turnover24h, ParseOperator(op), value, to);

        SavedFilterMatch.Pair([condition], Row(None with { Turnover24h = Number(actual) }), NoSetups).ShouldBe(expected);
    }

    private static IEnumerable<TestCaseData> MetricFields()
    {
        yield return Field(FilterField.Change5m, (metrics, value) => metrics with { Change5m = value });
        yield return Field(FilterField.Change15m, (metrics, value) => metrics with { Change15m = value });
        yield return Field(FilterField.Change1h, (metrics, value) => metrics with { Change1h = value });
        yield return Field(FilterField.Change4h, (metrics, value) => metrics with { Change4h = value });
        yield return Field(FilterField.Change24h, (metrics, value) => metrics with { Change24h = value });
        yield return Field(FilterField.Turnover1h, (metrics, value) => metrics with { Turnover1h = value });
        yield return Field(FilterField.Turnover24h, (metrics, value) => metrics with { Turnover24h = value });
        yield return Field(FilterField.OpenInterest, (metrics, value) => metrics with { OpenInterest = value });
        yield return Field(FilterField.OpenInterestChange1h, (metrics, value) => metrics with { OpenInterestChange1h = value });
        yield return Field(FilterField.OpenInterestChange24h, (metrics, value) => metrics with { OpenInterestChange24h = value });
        yield return Field(FilterField.FundingRate, (metrics, value) => metrics with { FundingRate = value });
        yield return Field(FilterField.Spread, (metrics, value) => metrics with { Spread = value });
    }

    [TestCaseSource(nameof(MetricFields))]
    public void Each_metric_field_tests_its_own_metric(FilterField field, Func<PairMetrics, decimal, PairMetrics> set)
    {
        FilterCondition[] conditions = [Compare(field, FilterOperator.Greater, 5m)];

        SavedFilterMatch.Pair(conditions, Row(set(None, 10m)), NoSetups).ShouldBeTrue();
        SavedFilterMatch.Pair(conditions, Row(set(None, 1m)), NoSetups).ShouldBeFalse();
        SavedFilterMatch.Pair(conditions, Row(set(None with { Change1h = 10m }, 1m)), NoSetups).ShouldBeFalse();
    }

    private static TestCaseData Field(FilterField field, Func<PairMetrics, decimal, PairMetrics> set)
    {
        return new TestCaseData(field, set).SetName($"Each metric field tests its own metric: {field}");
    }

    // Scenario "Выбор фильтра на рынке": percent as the row has it and funding as a fraction.
    [Test]
    public void A_pair_must_meet_every_condition()
    {
        FilterCondition[] conditions = [Compare(FilterField.Change1h, FilterOperator.GreaterOrEqual, 3m), Compare(FilterField.FundingRate, FilterOperator.Greater, 0.0003m)];

        SavedFilterMatch.Pair(conditions, Row(None with { Change1h = 3.5m, FundingRate = 0.0004m }), NoSetups).ShouldBeTrue();
        SavedFilterMatch.Pair(conditions, Row(None with { Change1h = 2.9m, FundingRate = 0.0004m }), NoSetups).ShouldBeFalse();
        SavedFilterMatch.Pair(conditions, Row(None with { Change1h = 3.5m, FundingRate = 0.0002m }), NoSetups).ShouldBeFalse();
    }

    [Test]
    public void The_digits_are_compared_exactly()
    {
        FilterCondition[] conditions = [Compare(FilterField.OpenInterest, FilterOperator.Greater, 0.1234567890123456789m)];

        SavedFilterMatch.Pair(conditions, Row(None with { OpenInterest = 0.1234567890123456790m }), NoSetups).ShouldBeTrue();
        SavedFilterMatch.Pair(conditions, Row(None with { OpenInterest = 0.1234567890123456788m }), NoSetups).ShouldBeFalse();
    }

    [Test]
    public void A_pair_without_a_value_for_the_field_does_not_pass()
    {
        SavedFilterMatch.Pair([Compare(FilterField.OpenInterest, FilterOperator.Greater, 1m)], Row(None), NoSetups).ShouldBeFalse();
    }

    // Conditions on idea fields act only on «Идеи».
    [Test]
    public void The_conditions_on_idea_fields_are_skipped_for_a_pair()
    {
        FilterCondition[] conditions =
        [
            Compare(FilterField.Probability, FilterOperator.Greater, 0.9m),
            Compare(FilterField.Expectancy, FilterOperator.ReliablyPositive, null),
            Compare(FilterField.Expectancy, FilterOperator.Greater, 5m),
            Compare(FilterField.RiskReward, FilterOperator.Greater, 5m),
            Is(FilterField.IdeaSide, "short"),
            Is(FilterField.IdeaSetup, "drop"),
            Is(FilterField.IdeaType, "levels"),
            Is(FilterField.IdeaInterval, "240"),
            Compare(FilterField.Change1h, FilterOperator.Greater, 1m)
        ];

        SavedFilterMatch.Pair(conditions, Row(None with { Change1h = 2m }), NoSetups).ShouldBeTrue();
    }

    [Test]
    public void The_category_of_a_pair_is_tested_by_its_code()
    {
        FilterCondition[] conditions = [Is(FilterField.Category, "spot")];

        SavedFilterMatch.Pair(conditions, Row(None, MarketCategory.Spot), NoSetups).ShouldBeTrue();
        SavedFilterMatch.Pair(conditions, Row(None, MarketCategory.Linear), NoSetups).ShouldBeFalse();
    }

    [Test]
    public void The_setup_of_a_pair_holds_on_any_interval()
    {
        FilterCondition[] conditions = [Is(FilterField.Setup, "drop")];
        PairSetup[] setups = [new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse), new PairSetup(Interval.OneHour, IdeaSetup.Drop), new PairSetup(Interval.OneHour, IdeaSetup.Impulse)];

        SavedFilterMatch.Pair(conditions, Row(None), setups).ShouldBeTrue();
        SavedFilterMatch.Pair(conditions, Row(None), [setups[0], setups[2]]).ShouldBeFalse();
    }

    // Scenario "Условие R:R в фильтре": the price ratio of the idea is 2, after the fees it is 1.56.
    [Test]
    public void An_idea_is_tested_by_its_risk_reward_after_the_fees_probability_and_expectancy()
    {
        IdeaView idea = View(IdeaViewGenerator.Bucket(0.38m, 0.46m), new IdeaShares(0.0156m, 0.01m, 0.01m));

        Passes(idea, Compare(FilterField.RiskReward, FilterOperator.GreaterOrEqual, 1.5m)).ShouldBeTrue();
        Passes(idea, Compare(FilterField.RiskReward, FilterOperator.GreaterOrEqual, 1.9m)).ShouldBeFalse();
        Passes(idea, Compare(FilterField.Probability, FilterOperator.Greater, 0.45m)).ShouldBeTrue();
        Passes(idea, Compare(FilterField.Probability, FilterOperator.Greater, 0.47m)).ShouldBeFalse();
        Passes(idea, Compare(FilterField.Expectancy, FilterOperator.Greater, 0.3m)).ShouldBeTrue();
    }

    [Test]
    public void An_idea_is_tested_by_the_codes_of_its_setup_type_interval_and_side()
    {
        IdeaView idea = View(null);

        Passes(idea, Is(FilterField.IdeaSetup, "impulse")).ShouldBeTrue();
        Passes(idea, Is(FilterField.IdeaSetup, "drop")).ShouldBeFalse();
        Passes(idea, Is(FilterField.IdeaType, "target_2r")).ShouldBeTrue();
        Passes(idea, Is(FilterField.IdeaType, "levels")).ShouldBeFalse();
        Passes(idea, Is(FilterField.IdeaInterval, "15")).ShouldBeTrue();
        Passes(idea, Is(FilterField.IdeaInterval, "60")).ShouldBeFalse();
        Passes(idea, Is(FilterField.IdeaSide, "long")).ShouldBeTrue();
        Passes(idea, Is(FilterField.IdeaSide, "short")).ShouldBeFalse();
    }

    [Test]
    public void An_idea_is_tested_by_the_category_and_setups_of_the_row_of_its_pair()
    {
        MarketSnapshotRow spot = Row(None, MarketCategory.Spot);
        PairSetup[] setups = [new PairSetup(Interval.OneHour, IdeaSetup.Drop)];

        SavedFilterMatch.Idea([Is(FilterField.Category, "spot")], View(null), spot, setups).ShouldBeTrue();
        SavedFilterMatch.Idea([Is(FilterField.Category, "linear")], View(null), spot, setups).ShouldBeFalse();
        SavedFilterMatch.Idea([Is(FilterField.Setup, "drop")], View(null), spot, setups).ShouldBeTrue();
        SavedFilterMatch.Idea([Is(FilterField.Setup, "impulse")], View(null), spot, setups).ShouldBeFalse();
        SavedFilterMatch.Idea([Is(FilterField.Setup, "drop")], View(null), null, setups).ShouldBeFalse();
    }

    [Test]
    public void An_idea_whose_bucket_has_no_case_does_not_pass_a_condition_on_the_bucket()
    {
        Passes(View(null), Compare(FilterField.Expectancy, FilterOperator.Greater, -5m)).ShouldBeFalse();
    }

    // A bucket of «мало данных» has no probability of the model, so its idea has none (decision 5 of add-own-idea-numbers).
    [Test]
    public void An_idea_with_too_little_data_does_not_pass_a_condition_on_its_probability()
    {
        IdeaView idea = View(IdeaViewGenerator.Bucket(0.3m, cases: 12));

        Passes(idea, Compare(FilterField.Probability, FilterOperator.Greater, 0m)).ShouldBeFalse();
    }

    // Scenario "Надёжно выше нуля": only the badge ▲ passes, not ≈ nor a bucket of too little data.
    [Test]
    public void Reliably_positive_holds_when_the_badge_of_the_bucket_is_up()
    {
        FilterCondition reliably = Compare(FilterField.Expectancy, FilterOperator.ReliablyPositive, null);

        Passes(View(IdeaViewGenerator.Bucket(0.12m, 0.46m, expectancyLow: 0.002m, expectancyHigh: 0.238m)), reliably).ShouldBeTrue();
        Passes(View(IdeaViewGenerator.Bucket(0.05m, 0.46m, expectancyLow: -0.068m, expectancyHigh: 0.168m)), reliably).ShouldBeFalse();
        Passes(View(IdeaViewGenerator.Bucket(0.3m, cases: 12)), reliably).ShouldBeFalse();
        Passes(View(null), reliably).ShouldBeFalse();
    }

    // Scenario "Ожидание идеи, а не корзины" (decision 5 of add-own-idea-numbers): the condition compares the expectancy of the idea in R.
    [Test]
    public void Expectancy_condition_compares_the_expectancy_of_the_idea_not_of_its_bucket()
    {
        FilterCondition above = Compare(FilterField.Expectancy, FilterOperator.Greater, -0.1m);
        BucketStatistics bucket = IdeaViewGenerator.Bucket(-0.21m, 0.45m);

        Passes(View(bucket, numbers: IdeaViewGenerator.Numbers(-0.72m, -0.00216m)), above).ShouldBeFalse();
        Passes(View(bucket, numbers: IdeaViewGenerator.Numbers(-0.062m, -0.0031m)), above).ShouldBeTrue();
    }

    // Scenario "Вероятность идеи «Уровни»": the probability of the idea, 0.22 × 0.675, not that of its bucket.
    [Test]
    public void Probability_condition_compares_the_probability_of_the_idea()
    {
        IdeaView idea = View(IdeaViewGenerator.Bucket(-0.21m, 0.22m), numbers: IdeaViewGenerator.Numbers(-0.1m, probability: 0.1485m));

        Passes(idea, Compare(FilterField.Probability, FilterOperator.GreaterOrEqual, 0.2m)).ShouldBeFalse();
    }

    // Scenario "Надёжно выше нуля": two ideas of one bucket of ≈, ▲ by the interval of the first and ≈ by that of the second.
    [Test]
    public void Reliably_positive_holds_when_the_badge_of_the_idea_is_up()
    {
        FilterCondition reliably = Compare(FilterField.Expectancy, FilterOperator.ReliablyPositive, null);
        BucketStatistics bucket = IdeaViewGenerator.Bucket(0.05m, 0.46m, expectancyLow: -0.068m, expectancyHigh: 0.168m);

        Passes(View(bucket, numbers: IdeaViewGenerator.Numbers(0.058m, 0.0029m, 0.018m, 0.098m)), reliably).ShouldBeTrue();
        Passes(View(bucket, numbers: IdeaViewGenerator.Numbers(-0.005m, -0.0001m, -0.045m, 0.035m)), reliably).ShouldBeFalse();
    }

    // Scenario "Идея пары без строки снимка".
    [Test]
    public void An_idea_is_tested_by_the_metrics_of_the_row_of_its_pair_and_fails_without_a_row()
    {
        FilterCondition[] conditions = [Compare(FilterField.Turnover24h, FilterOperator.Greater, 50000000m)];

        SavedFilterMatch.Idea(conditions, View(null), Row(None with { Turnover24h = 60000000m }), NoSetups).ShouldBeTrue();
        SavedFilterMatch.Idea(conditions, View(null), Row(None with { Turnover24h = 1m }), NoSetups).ShouldBeFalse();
        SavedFilterMatch.Idea(conditions, View(null), null, NoSetups).ShouldBeFalse();
    }

    [Test]
    public void An_idea_must_meet_every_condition()
    {
        FilterCondition[] conditions = [Compare(FilterField.RiskReward, FilterOperator.GreaterOrEqual, 1.5m), Is(FilterField.IdeaSide, "short")];

        SavedFilterMatch.Idea(conditions, View(null), Row(None), NoSetups).ShouldBeFalse();
    }

    private static bool Passes(IdeaView idea, FilterCondition condition)
    {
        return SavedFilterMatch.Idea([condition], idea, Row(None), NoSetups);
    }

    private static IdeaView View(BucketStatistics? bucket, IdeaShares? shares = null, IdeaNumbers? numbers = null)
    {
        return IdeaViewGenerator.Generate(IdeaGenerator.Generate(DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture)), bucket, shares, numbers);
    }

    private static MarketSnapshotRow Row(PairMetrics metrics, MarketCategory category = MarketCategory.Linear)
    {
        return MarketSnapshotRowGenerator.Generate(metrics: metrics, category: category);
    }

    private static FilterCondition Compare(FilterField field, FilterOperator op, decimal? value, decimal? to = null)
    {
        return new FilterCondition(field, op, value, to);
    }

    private static FilterCondition Is(FilterField field, string choice)
    {
        return new FilterCondition(field, FilterOperator.Equal, Choice: choice);
    }

    private static FilterOperator ParseOperator(string code)
    {
        SavedFilterFields.TryParseOperator(code, out FilterOperator op);

        return op;
    }

    private static decimal Number(string text)
    {
        return decimal.Parse(text, CultureInfo.InvariantCulture);
    }
}