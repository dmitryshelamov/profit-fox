using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Tests.Unit.Domain.Services.Lists;

/// <summary>
///     Change add-ideas-and-market, task 10.1: the request of a page of a list — a field of sorting of the list only, a
///     direction, a page and a size of 50, 100 or 200.
/// </summary>
public sealed class ListFieldsTests
{
    [Test]
    public void A_field_outside_the_list_is_an_error_of_the_sorting()
    {
        List<FilterProblem> problems = [];

        ListOrder? order = ListFields.Order(ListFields.Ideas, "moment", "secret", null, null, null, problems);

        Assert.That(order, Is.Null);
        Assert.That(problems.Select(problem => (problem.Code, problem.Field)), Is.EqualTo(new[] { ("UnknownField", (string?)"sort") }));
    }

    [Test]
    public void Without_a_field_the_list_sorts_by_its_default_a_number_descends_and_a_text_ascends()
    {
        List<FilterProblem> problems = [];

        ListOrder market = ListFields.Order(ListFields.Market, "turnover24h", null, null, null, null, problems)!;
        ListOrder symbol = ListFields.Order(ListFields.Market, "turnover24h", "symbol", null, 2, 100, problems)!;
        ListOrder ascending = ListFields.Order(ListFields.Market, "turnover24h", "fundingRate", "asc", null, null, problems)!;

        Assert.That(problems, Is.Empty);
        Assert.That(market, Is.EqualTo(new ListOrder("turnover24h", Descending: true, 1, 50)));
        Assert.That(symbol, Is.EqualTo(new ListOrder("symbol", Descending: false, 2, 100)));
        Assert.That(symbol.Offset, Is.EqualTo(100));
        Assert.That(ascending.Descending, Is.False);
    }

    [TestCase(0, 50, "page")]
    [TestCase(1, 30, "pageSize")]
    public void A_page_below_1_and_a_size_not_of_the_three_are_refused(int page, int size, string field)
    {
        List<FilterProblem> problems = [];

        Assert.That(ListFields.Order(ListFields.Market, "turnover24h", null, "up", page, size, problems), Is.Null);
        Assert.That(problems.Select(problem => problem.Field), Is.EquivalentTo(new[] { "direction", field }));
    }
}
