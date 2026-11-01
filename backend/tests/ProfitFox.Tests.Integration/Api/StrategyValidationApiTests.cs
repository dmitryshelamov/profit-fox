using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class StrategyValidationApiTests
{
    private const string Strategies = "/api/strategies";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.Bybit.Instruments.UnionWith([("spot", "BTCUSDT")]);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Валидная новая стратегия".
    [Test]
    public async Task Valid_new_strategy_has_no_errors_and_is_not_saved()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync($"{Strategies}/validate", new { name = "EMA", definition = StrategyFixtures.Element("ema") });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("errors").EnumerateObject().ShouldBeEmpty();
        result.GetProperty("usages").GetArrayLength().ShouldBe(0);
        result.GetProperty("bots").GetArrayLength().ShouldBe(0);
        (await _client.GetFromJsonAsync<JsonElement>(Strategies)).GetArrayLength().ShouldBe(0);
    }

    // Scenario "Ошибка графа".
    [Test]
    public async Task Graph_error_is_reported_and_the_strategy_stays_as_it_was()
    {
        Guid id = await CreateStrategyAsync();
        JsonNode definition = JsonNode.Parse(StrategyFixtures.Json("ema"))!;
        definition["wires"]!.AsArray().RemoveAt(6);

        JsonElement result = await ValidateAsync($"{Strategies}/{id}/validate", definition);

        result.GetProperty("errors").GetProperty("graph.enter.when").GetArrayLength().ShouldBe(1);
        JsonElement strategy = await _client.GetFromJsonAsync<JsonElement>($"{Strategies}/{id}");
        strategy.GetProperty("version").GetInt32().ShouldBe(1);
        JsonNode.DeepEquals(JsonNode.Parse(strategy.GetProperty("definition").GetRawText()), JsonNode.Parse(StrategyFixtures.Json("ema"))).ShouldBeTrue();
    }

    // Scenario "Правка ломает бота".
    [Test]
    public async Task Change_that_breaks_a_bot_lists_the_bot_and_changes_nothing()
    {
        Guid id = await CreateStrategyAsync();
        Guid bot = await CreateBotAsync(id);
        JsonNode definition = JsonNode.Parse(StrategyFixtures.Json("ema"))!;
        definition["parameters"]!.AsArray().RemoveAt(0);
        definition["wires"]!.AsArray().RemoveAt(1);
        definition["nodes"]![3]!["inputs"] = new JsonObject { ["fast"] = 5 };

        JsonElement result = await ValidateAsync($"{Strategies}/{id}/validate", definition);

        result.GetProperty("errors").EnumerateObject().ShouldBeEmpty();
        JsonElement affected = result.GetProperty("bots").EnumerateArray().ShouldHaveSingleItem();
        (affected.GetProperty("id").GetGuid(), affected.GetProperty("name").GetString()).ShouldBe((bot, "EMA BTC"));
        affected.GetProperty("errors").GetProperty("parameterValues.fast").GetString().ShouldBe("No such parameter.");
        (await _client.GetFromJsonAsync<JsonElement>($"{Strategies}/{id}")).GetProperty("version").GetInt32().ShouldBe(1);
    }

    // Scenario "Неизвестная стратегия".
    [Test]
    public async Task Change_of_an_unknown_strategy_is_not_found()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync($"{Strategies}/{Guid.NewGuid()}/validate", new { name = "EMA", definition = StrategyFixtures.Element("ema") });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Malformed_definition_is_reported_as_errors()
    {
        JsonElement definition = JsonDocument.Parse("""{ "formatVersion": 2, "direction": "long" }""").RootElement;

        JsonElement result = await ValidateAsync($"{Strategies}/validate", JsonNode.Parse(definition.GetRawText())!);

        result.GetProperty("errors").TryGetProperty("definition.formatVersion", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Empty_name_is_rejected_like_a_save()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync($"{Strategies}/validate", new { name = "", definition = StrategyFixtures.Element("ema") });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("name", out _).ShouldBeTrue();
    }

    private async Task<JsonElement> ValidateAsync(string url, JsonNode definition)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(url, new { name = "EMA", definition });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateStrategyAsync()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Strategies, new { name = "EMA", definition = StrategyFixtures.Element("ema") });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBotAsync(Guid strategyId)
    {
        var body = new JsonObject
        {
            ["name"] = "EMA BTC",
            ["strategyId"] = strategyId,
            ["parameterValues"] = new JsonObject { ["fast"] = 10 },
            ["category"] = "spot",
            ["symbol"] = "BTCUSDT",
            ["interval"] = "60",
            ["balance"] = 1000m
        };
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/bots", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}