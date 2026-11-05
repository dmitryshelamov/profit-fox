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

    [Test]
    public void A_wrong_condition_is_an_error_keyed_by_its_place()
    {
        var errors = new Dictionary<string, string>();

        SavedFilterRules.ParseConditions([new SavedFilterConditionInput("turnover24h", ">", "1", null), new SavedFilterConditionInput("nope", ">", "1", null)], errors);

        errors.Keys.ShouldBe(["conditions[1].field"]);
    }
}