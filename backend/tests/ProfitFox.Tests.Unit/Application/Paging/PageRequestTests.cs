using ProfitFox.Application.Paging;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Paging;

/// <summary>Requirements "Чтение идей" and "Чтение снимка рынка": the page of a list.</summary>
public class PageRequestTests
{
    private static readonly int[] Rows = [.. Enumerable.Range(1, 120)];

    // Scenario "Страница идей".
    [Test]
    public void The_third_page_of_50_holds_the_last_20_rows()
    {
        Page<int> page = new PageRequest(3, 50).Slice(Rows);

        page.Items.ShouldBe([.. Enumerable.Range(101, 20)]);
        (page.Number, page.Size, page.Total).ShouldBe((3, 50, 120));
    }

    [Test]
    public void The_first_page_holds_the_first_50_rows()
    {
        Page<int> page = new PageRequest(1, 50).Slice(Rows);

        page.Items.ShouldBe([.. Enumerable.Range(1, 50)]);
        page.Number.ShouldBe(1);
    }

    // Scenario "Страница за концом списка".
    [Test]
    public void A_page_past_the_end_gives_the_last_page()
    {
        Page<int> page = new PageRequest(5, 50).Slice(Rows);

        page.Items.ShouldBe([.. Enumerable.Range(101, 20)]);
        page.Number.ShouldBe(3);
    }

    [Test]
    public void An_empty_list_gives_the_first_page_without_rows()
    {
        Page<int> page = new PageRequest(4, 50).Slice<int>([]);

        page.Items.ShouldBeEmpty();
        (page.Number, page.Total).ShouldBe((1, 0));
    }

    [Test]
    public void A_full_last_page_is_not_followed_by_an_empty_one()
    {
        Page<int> page = new PageRequest(3, 50).Slice([.. Enumerable.Range(1, 100)]);

        page.Number.ShouldBe(2);
        page.Items.Count.ShouldBe(50);
    }

    [Test]
    public void Nothing_asked_gives_the_first_page_of_50()
    {
        var errors = new Dictionary<string, string>();

        var request = PageRequest.Parse(null, null, errors);

        request.ShouldBe(new PageRequest(1, 50));
        errors.ShouldBeEmpty();
    }

    [TestCase(100)]
    [TestCase(200)]
    public void A_listed_size_is_taken(int size)
    {
        var errors = new Dictionary<string, string>();

        PageRequest.Parse(2, size, errors).ShouldBe(new PageRequest(2, size));
        errors.ShouldBeEmpty();
    }

    // Scenario "Неизвестный размер страницы".
    [TestCase(75)]
    [TestCase(0)]
    public void Another_size_is_an_error_of_the_page_size(int size)
    {
        var errors = new Dictionary<string, string>();

        PageRequest.Parse(null, size, errors);

        errors.Keys.ShouldBe(["pageSize"]);
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void A_number_below_1_is_an_error_of_the_page(int number)
    {
        var errors = new Dictionary<string, string>();

        PageRequest.Parse(number, null, errors);

        errors.Keys.ShouldBe(["page"]);
    }
}