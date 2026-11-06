using ProfitFox.Application.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.SavedFilters;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.SavedFilters;

/// <summary>Requirement "Подгонка черновика фильтра": the conditions are checked by the rules of saving, without a name, a place or a minimum of conditions.</summary>
public class SavedFilterRulesTests
{
    [Test]
    public void No_conditions_are_an_empty_list_without_errors()
    {
        var errors = new Dictionary<string, string>();

        SavedFilterRules.ParseConditions([], errors).ShouldBeEmpty();
        SavedFilterRules.ParseConditions(null, errors).ShouldBeEmpty();

        errors.ShouldBeEmpty();
    }

    [Test]
    public void A_condition_is_read_by_the_catalog()
    {
        var errors = new Dictionary<string, string>();

        IReadOnlyList<FilterCondition> conditions = SavedFilterRules.ParseConditions(
            [new SavedFilterConditionInput("turnover24h", "between", "1", "3"), new SavedFilterConditionInput("side", "=", "long", null)],
            errors);

        conditions.ShouldBe([new FilterCondition(FilterField.Turnover24h, FilterOperator.Between, 1m, 3m), new FilterCondition(FilterField.IdeaSide, FilterOperator.Equal, Choice: "long")]);
        errors.ShouldBeEmpty();
    }

    // Task 7.3: a saved filter offers the candle setups and «Стоп-вход», in the order of the lists on the screen.
    [Test]
    public void Catalog_offers_every_setup_and_type()
    {
        string[] setups = ["impulse", "early_impulse", "drop", "early_drop", "high_volatility", "breakout", "false_breakout", "squeeze", "exhaustion", "rsi_extreme", "new_extreme", "quiet_volume"];

        SavedFilterFields.All.Single(field => field.Code == "setup").Choices.ShouldBe(setups);
        SavedFilterFields.All.Single(field => field.Code == "ideaSetup").Choices.ShouldBe(setups);
        SavedFilterFields.All.Single(field => field.Code == "type").Choices.ShouldBe(["levels", "target_2r", "beyond_extreme", "pullback", "stop_entry"]);
    }

    [Test]
    public void A_condition_on_a_false_breakout_is_read()
    {
        var errors = new Dictionary<string, string>();

        SavedFilterRules.ParseConditions([new SavedFilterConditionInput("ideaSetup", "=", "false_breakout", null)], errors)
            .ShouldBe([new FilterCondition(FilterField.IdeaSetup, FilterOperator.Equal, Choice: "false_breakout")]);
        errors.ShouldBeEmpty();
    }

    [Test]
    public void A_wrong_condition_is_an_error_keyed_by_its_place()
    {
        var errors = new Dictionary<string, string>();

        SavedFilterRules.ParseConditions([new SavedFilterConditionInput("turnover24h", ">", "1", null), new SavedFilterConditionInput("nope", ">", "1", null)], errors);

        errors.Keys.ShouldBe(["conditions[1].field"]);
    }

    // Scenario "Надёжно выше нуля": the operator takes no value, and a value sent with it is dropped.
    [Test]
    public void Reliably_positive_is_read_without_a_value_on_the_expectancy()
    {
        var errors = new Dictionary<string, string>();

        IReadOnlyList<FilterCondition> conditions = SavedFilterRules.ParseConditions([new SavedFilterConditionInput("expectancy", "reliably_positive", "1", null)], errors);

        conditions.ShouldBe([new FilterCondition(FilterField.Expectancy, FilterOperator.ReliablyPositive)]);
        errors.ShouldBeEmpty();
    }

    // Scenario "Старый оператор": «выше базовой» is no operator any more, on the probability or anywhere.
    [TestCase("probability", "above_baseline")]
    [TestCase("probability", "reliably_positive")]
    public void Above_the_baseline_and_reliably_positive_off_the_expectancy_are_errors(string field, string op)
    {
        var errors = new Dictionary<string, string>();

        SavedFilterRules.ParseConditions([new SavedFilterConditionInput(field, op, null, null)], errors).ShouldBeEmpty();

        errors.Keys.ShouldBe(["conditions[0].operator"]);
    }
}