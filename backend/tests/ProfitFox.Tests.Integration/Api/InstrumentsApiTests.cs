using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class InstrumentsApiTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        _factory.Bybit.Instruments.UnionWith(
            [("spot", "BTCUSDT"), ("spot", "ETHBTC"), ("spot", "ETHUSDT"), ("spot", "STETHUSDT"), ("spot", "SOLUSDT"), ("linear", "BTCUSDT")]);
        _factory.Bybit.Coins[("spot", "BTCUSDT")] = ("BTC", "USDT");
        _factory.Bybit.Coins[("spot", "ETHBTC")] = ("ETH", "BTC");
        _factory.Bybit.Coins[("spot", "ETHUSDT")] = ("ETH", "USDT");
        _factory.Bybit.Coins[("spot", "STETHUSDT")] = ("STETH", "USDT");
        _factory.Bybit.Coins[("spot", "SOLUSDT")] = ("SOL", "USDT");
        _factory.Bybit.Coins[("linear", "BTCUSDT")] = ("BTC", "USDT");
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Часть символа".
    [Test]
    public async Task Part_of_the_symbol_finds_instruments_with_their_coins()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/market-data/instruments?category=spot&query=btc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement[] found = (await response.Content.ReadFromJsonAsync<JsonElement[]>())!;
        found.Select(instrument => instrument.GetProperty("symbol").GetString()).ShouldBe(["BTCUSDT", "ETHBTC"]);
        found[0].GetRawText().ShouldBe("""{"category":"spot","symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT"}""");
    }

    // Scenario "Ничего не найдено".
    [Test]
    public async Task Nothing_found_is_an_empty_list()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/market-data/instruments?category=linear&query=NOPE");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement[]>())!.ShouldBeEmpty();
    }

    // Scenario "Сначала начинающиеся с запроса".
    [Test]
    public async Task Symbols_starting_with_the_query_come_first()
    {
        JsonElement[] found = (await _client.GetFromJsonAsync<JsonElement[]>("/api/market-data/instruments?category=spot&query=ETH"))!;

        found.Select(instrument => instrument.GetProperty("symbol").GetString()).ShouldBe(["ETHBTC", "ETHUSDT", "STETHUSDT"]);
    }

    [Test]
    public async Task Limit_cuts_the_list()
    {
        JsonElement[] found = (await _client.GetFromJsonAsync<JsonElement[]>("/api/market-data/instruments?category=spot&query=usdt&limit=2"))!;

        found.Length.ShouldBe(2);
    }

    // Scenarios "Лимит вне границ", "Неподдерживаемая категория" and the empty query.
    [TestCase("category=spot&query=btc&limit=500", "limit")]
    [TestCase("category=spot&query=btc&limit=0", "limit")]
    [TestCase("category=option&query=btc", "category")]
    [TestCase("category=spot&query=", "query")]
    [TestCase("category=spot", "query")]
    [TestCase("query=btc", "category")]
    public async Task Invalid_request_is_rejected_with_the_invalid_parameter(string query, string invalidField)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/market-data/instruments?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe([invalidField]);
    }

    // Scenario "Bybit не отвечает".
    [Test]
    public async Task Unavailable_bybit_means_503()
    {
        _factory.Bybit.Unavailable = true;

        HttpResponseMessage response = await _client.GetAsync("/api/market-data/instruments?category=spot&query=btc");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task Rejected_list_request_means_503()
    {
        _factory.Bybit.RejectLists = true;

        HttpResponseMessage response = await _client.GetAsync("/api/market-data/instruments?category=spot&query=btc");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task Second_search_within_five_minutes_does_not_ask_bybit_and_one_after_five_minutes_does()
    {
        await _client.GetAsync("/api/market-data/instruments?category=spot&query=btc");
        await _client.GetAsync("/api/market-data/instruments?category=spot&query=eth");
        _factory.Bybit.ListRequests.ShouldBe(["spot"]);

        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        await _client.GetAsync("/api/market-data/instruments?category=spot&query=eth");

        _factory.Bybit.ListRequests.ShouldBe(["spot", "spot"]);
    }
}