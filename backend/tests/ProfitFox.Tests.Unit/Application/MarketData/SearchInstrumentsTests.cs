using NSubstitute;

using ProfitFox.Application.MarketData.Queries.SearchInstruments;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class SearchInstrumentsTests
{
    private IInstrumentCatalog _catalog = null!;
    private SearchInstrumentsQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = Substitute.For<IInstrumentCatalog>();
        _catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(
            [
                new ListedInstrument(MarketCategory.Linear, "ETHUSDT", "ETH", "USDT"),
                new ListedInstrument(MarketCategory.Linear, "BTCUSDT", "BTC", "USDT")
            ]);
        _handler = new SearchInstrumentsQueryHandler(_catalog);
    }

    [Test]
    public async Task Returns_the_listed_instruments_of_the_category_that_match()
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", "btc", Limit: null), CancellationToken.None);

        result.Errors.ShouldBeEmpty();
        result.Instruments.ShouldBe([new ListedInstrument(MarketCategory.Linear, "BTCUSDT", "BTC", "USDT")]);
    }

    [Test]
    public async Task Spaces_around_the_query_are_ignored()
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", " BTCUSDT ", Limit: null), CancellationToken.None);

        result.Instruments.ShouldBe([new ListedInstrument(MarketCategory.Linear, "BTCUSDT", "BTC", "USDT")]);
    }

    [Test]
    public async Task Limit_defaults_to_twenty()
    {
        var many = Enumerable.Range(0, 30).Select(i => new ListedInstrument(MarketCategory.Linear, $"COIN{i:00}USDT", $"COIN{i:00}", "USDT")).ToList();
        _catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(many);

        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", "coin", Limit: null), CancellationToken.None);

        result.Instruments!.Count.ShouldBe(20);
    }

    [Test]
    public async Task Limit_is_applied()
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", "usdt", Limit: 1), CancellationToken.None);

        result.Instruments!.Select(instrument => instrument.Symbol).ShouldBe(["BTCUSDT"]);
    }

    // Scenario "Лимит вне границ".
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(101)]
    [TestCase(500)]
    public async Task Limit_outside_one_to_one_hundred_is_an_error_of_the_limit(int limit)
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", "btc", limit), CancellationToken.None);

        result.Instruments.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["limit"]);
    }

    [TestCase(1)]
    [TestCase(100)]
    public async Task Limit_bounds_are_allowed(int limit)
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", "btc", limit), CancellationToken.None);

        result.Errors.ShouldBeEmpty();
    }

    // Scenario "Неподдерживаемая категория".
    [Test]
    public async Task Unsupported_category_is_an_error_of_the_category()
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("option", "btc", Limit: null), CancellationToken.None);

        result.Instruments.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["category"]);
        await _catalog.DidNotReceiveWithAnyArgs().ListAsync(default, default);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Blank_query_is_an_error_of_the_query(string query)
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("linear", query, Limit: null), CancellationToken.None);

        result.Errors.Keys.ShouldBe(["query"]);
    }

    [Test]
    public async Task Every_invalid_field_is_reported()
    {
        SearchInstrumentsResult result = await _handler.Handle(new SearchInstrumentsQuery("option", "", 0), CancellationToken.None);

        result.Errors.Keys.ShouldBe(["category", "query", "limit"], ignoreOrder: true);
    }
}