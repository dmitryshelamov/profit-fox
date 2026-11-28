using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Lists;

/// <summary>
///     Change add-ideas-and-market, task 10.2: spec saved-filters «Сохранённый фильтр», «Условие фильтра» and «Поля фильтра».
/// </summary>
public sealed class FilterRulesTests
{
    [Test]
    public void A_filter_with_a_name_a_place_and_its_conditions_passes()
    {
        List<FilterProblem> problems = FilterRules.Check("Высокий фандинг", "market", [new FilterCondition("fundingRate", "ge", "0.03"), new FilterCondition("turnover24h", "gt", "100")]);

        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void A_filter_without_conditions_is_refused_on_its_conditions()
    {
        List<FilterProblem> problems = FilterRules.Check("Пустой", "market", []);

        Assert.That(problems.Select(problem => (problem.Code, problem.Field, problem.Condition)), Is.EqualTo(new[] { ("NoConditions", (string?)"conditions", (int?)null) }));
    }

    [Test]
    public void A_name_and_a_place_that_are_not_are_refused_on_their_fields()
    {
        List<FilterProblem> problems = FilterRules.Check(" ", "everywhere", [new FilterCondition("price", "gt", "1")]);

        Assert.That(problems.Select(problem => problem.Field), Is.EqualTo(new[] { "name", "place" }));
    }

    /// <summary>
    ///     Change add-idea-score, task 2.5, and change decimal-label-weights, task 2.6: spec saved-filters «Поля фильтра»,
    ///     «Дробный балл в фильтре» — the score is an idea field of numbers with at most two digits after the point.
    /// </summary>
    [Test]
    public void The_score_is_a_field_of_an_idea_and_takes_a_number_with_at_most_two_digits_after_the_point()
    {
        ListField score = ListFields.Filter.Single(field => field.Name == "score");

        Assert.That((score.Type, score.Place, score.Unit), Is.EqualTo((FieldType.Number, FieldPlace.Idea, (string?)null)));
        Assert.That(
            FilterRules.CheckConditions([new FilterCondition("score", "ge", "2"), new FilterCondition("score", "between", "-1", "3"), new FilterCondition("score", "ge", "1.25"), new FilterCondition("score", "between", "-0.5", "0,75")]),
            Is.Empty);
        FilterProblem[] problems = [.. FilterRules.CheckConditions([new FilterCondition("score", "ge", "0.125"), new FilterCondition("score", "between", "0", "1.005")])];
        Assert.That(problems.Select(problem => problem.Condition), Is.EqualTo(new int?[] { 0, 1 }));
        Assert.That(problems.Select(problem => problem.Reason).Distinct(), Is.EqualTo(new[] { "значение поля Балл — число, не больше двух знаков после точки" }));
        Assert.That(ListFields.Ideas.Single(field => field.Name == "score").Type, Is.EqualTo(FieldType.Number), "the lists sort by it");
    }

    [Test]
    public void Between_takes_two_numbers_and_a_field_of_values_takes_one_of_them()
    {
        Assert.That(FilterRules.CheckConditions([new FilterCondition("change24h", "between", "-2", "2"), new FilterCondition("side", "eq", "short")]), Is.Empty);
    }

    [Test]
    public void An_operator_not_for_its_field_and_a_value_that_is_not_a_number_are_errors_of_their_conditions()
    {
        List<FilterProblem> problems = FilterRules.CheckConditions(
        [
            new FilterCondition("side", "gt", "short"),
            new FilterCondition("fundingRate", "ge", "0.01"),
            new FilterCondition("fundingRate", "ge", "abc"),
            new FilterCondition("secret", "eq", "1"),
            new FilterCondition("side", "eq", "sideways")
        ]);

        Assert.That(problems.Select(problem => (problem.Code, problem.Condition)), Is.EqualTo(new[]
        {
            ("InvalidCondition", (int?)0), ("InvalidCondition", 2), ("InvalidCondition", 3), ("InvalidCondition", 4)
        }));
    }

    /// <summary>
    ///     Task 18.17: a value in millions too large to keep is an error of its condition, not an overflow.
    /// </summary>
    [Test]
    public void A_value_too_large_for_its_unit_is_an_error_of_its_condition()
    {
        List<FilterProblem> problems = FilterRules.CheckConditions(
        [
            new FilterCondition("turnover24h", "gt", "100"),
            new FilterCondition("turnover24h", "gt", "8e22"),
            new FilterCondition("openInterest", "between", "1", "1e23")
        ]);

        Assert.That(problems.Select(problem => (problem.Code, problem.Condition)), Is.EqualTo(new[] { ("InvalidCondition", (int?)1), ("InvalidCondition", 2) }));
        Assert.That(problems[0].Reason, Does.Contain("слишком велико"));
    }

    [Test]
    public void The_funding_is_in_percent_and_the_turnover_in_millions()
    {
        ListField funding = ListFields.Filter.Single(field => field.Name == "fundingRate");
        ListField turnover = ListFields.Filter.Single(field => field.Name == "turnover24h");

        Assert.That((funding.Type, funding.Unit, FilterRules.Stored(funding, "0.03")), Is.EqualTo((FieldType.Number, (string?)"%", 0.0003m)));
        Assert.That((turnover.Unit, FilterRules.Stored(turnover, "100")), Is.EqualTo(((string?)"млн USDT", 100_000_000m)));
        Assert.That(FilterRules.Stored(funding, "0,05"), Is.EqualTo(0.0005m), "a comma as a point");
    }

    [Test]
    public void The_list_of_fields_has_those_of_the_market_and_of_an_idea_with_their_values()
    {
        Assert.That(ListFields.Filter.Where(field => field.Place == FieldPlace.Market).Select(field => field.Name), Is.EqualTo(new[]
        {
            "price", "change1h", "change4h", "change24h", "turnover24h", "openInterest", "fundingRate", "openIdeas"
        }));
        Assert.That(ListFields.Filter.Where(field => field.Place == FieldPlace.Idea).Select(field => field.Name), Is.EqualTo(new[]
        {
            "strategy", "timeframe", "side", "label", "score", "result", "minutesLeft", "hasStop"
        }));
        Assert.That(ListFields.Filter.Single(field => field.Name == "side").Values, Is.EqualTo(new[] { "long", "short" }));
    }

    [Test]
    public void A_filter_of_both_places_is_shown_in_each()
    {
        var both = new SavedFilter(Guid.NewGuid(), "Оба", FilterPlace.Both, []);
        SavedFilter market = both with { Place = FilterPlace.Market };

        Assert.That((both.ShownIn(FilterPlace.Ideas), market.ShownIn(FilterPlace.Ideas), market.ShownIn(FilterPlace.Market)), Is.EqualTo((true, false, true)));
    }
}
