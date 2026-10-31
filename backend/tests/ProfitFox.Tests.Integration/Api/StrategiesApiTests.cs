using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class StrategiesApiTests
{
    private const string Strategies = "/api/strategies";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [TestCase("ema", "long", new[] { "spot", "linear" })]
    [TestCase("ema-short", "short", new[] { "linear" })]
    [TestCase("dca", "long", new[] { "spot", "linear" })]
    [TestCase("ladder", "long", new[] { "spot", "linear" })]
    public async Task Reference_strategy_is_created_and_read_back_unchanged(string fixture, string direction, string[] categories)
    {
        HttpResponseMessage created = await Create(fixture, StrategyFixtures.Element(fixture));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Headers.Location!.ToString().ShouldEndWith($"{Strategies}/{id}");

        JsonElement strategy = await _client.GetFromJsonAsync<JsonElement>($"{Strategies}/{id}");
        strategy.GetProperty("name").GetString().ShouldBe(fixture);
        strategy.GetProperty("direction").GetString().ShouldBe(direction);
        strategy.GetProperty("supportedCategories").EnumerateArray().Select(category => category.GetString()).ShouldBe(categories);
        JsonNode.DeepEquals(JsonNode.Parse(strategy.GetProperty("definition").GetRawText()), JsonNode.Parse(StrategyFixtures.Json(fixture))).ShouldBeTrue();
    }

    [Test]
    public async Task List_gives_id_name_and_direction()
    {
        await Create("EMA", StrategyFixtures.Element("ema"));

        JsonElement list = await _client.GetFromJsonAsync<JsonElement>(Strategies);

        JsonElement item = list.EnumerateArray().ShouldHaveSingleItem();
        (item.GetProperty("name").GetString(), item.GetProperty("direction").GetString()).ShouldBe(("EMA", "long"));
    }

    [Test]
    public async Task Strategy_with_graph_errors_is_rejected_with_their_paths()
    {
        JsonElement definition = JsonDocument.Parse("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "mul", "kind": "multiply" } ] }""").RootElement;

        HttpResponseMessage response = await Create("x", definition);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty("graph.mul.a", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Strategy_without_a_name_or_definition_is_rejected()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Strategies, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue();
        errors.TryGetProperty("definition", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Unknown_strategy_is_not_found()
    {
        var id = Guid.NewGuid();

        (await _client.GetAsync($"{Strategies}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync($"{Strategies}/{id}", new { name = "x", definition = StrategyFixtures.Element("ema") })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.DeleteAsync($"{Strategies}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Strategy_is_changed_and_deleted()
    {
        Guid id = (await (await Create("EMA", StrategyFixtures.Element("ema"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await _client.PutAsJsonAsync($"{Strategies}/{id}", new { name = "EMA 2", definition = StrategyFixtures.Element("ema") })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<JsonElement>($"{Strategies}/{id}")).GetProperty("name").GetString().ShouldBe("EMA 2");

        (await _client.DeleteAsync($"{Strategies}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.GetAsync($"{Strategies}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Invalid_change_keeps_the_strategy()
    {
        Guid id = (await (await Create("EMA", StrategyFixtures.Element("ema"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        JsonElement cyclic = JsonDocument.Parse("""
            { "formatVersion": 1, "direction": "long",
              "nodes": [ { "id": "a", "kind": "add", "inputs": { "b": 1 } }, { "id": "m", "kind": "multiply", "inputs": { "b": 2 } } ],
              "wires": [ { "from": "a.result", "to": "m.a" }, { "from": "m.result", "to": "a.a" } ] }
            """).RootElement;

        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Strategies}/{id}", new { name = "EMA", definition = cyclic });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode.DeepEquals(
            JsonNode.Parse((await _client.GetFromJsonAsync<JsonElement>($"{Strategies}/{id}")).GetProperty("definition").GetRawText()),
            JsonNode.Parse(StrategyFixtures.Json("ema"))).ShouldBeTrue();
    }

    private Task<HttpResponseMessage> Create(string name, JsonElement definition)
    {
        return _client.PostAsJsonAsync(Strategies, new { name, definition });
    }
}