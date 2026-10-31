using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class StrategyNodesApiTests
{
    private const string Nodes = "/api/strategy-nodes";

    private const string Signal = """
        {
          "formatVersion": 1,
          "inputs": [ { "name": "x", "type": "number" } ],
          "outputs": [ { "name": "on", "type": "boolean" } ],
          "nodes": [
            { "id": "in", "kind": "inputs" },
            { "id": "above", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [ { "from": "in.x", "to": "above.a" }, { "from": "above.result", "to": "out.on" } ]
        }
        """;

    private const string WithoutOutput = """
        { "formatVersion": 1, "inputs": [ { "name": "x", "type": "number" } ], "outputs": [], "nodes": [] }
        """;

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

    [Test]
    public async Task Catalog_contains_simple_built_in_and_custom_nodes()
    {
        string id = await CreateSignal();

        JsonElement catalog = await _client.GetFromJsonAsync<JsonElement>(Nodes);

        string[] groups = [.. catalog.EnumerateArray().Select(node => node.GetProperty("group").GetString()!).Distinct()];
        groups.ShouldBe(["simple", "builtIn", "custom"]);
        JsonElement custom = catalog.EnumerateArray().Single(node => node.GetProperty("id").GetString() == id);
        custom.GetProperty("title").GetString().ShouldBe("Мой сигнал");
        custom.GetProperty("outputs")[0].GetProperty("type").GetString().ShouldBe("boolean");
    }

    [Test]
    public async Task Built_in_node_is_read_with_its_definition()
    {
        JsonElement ladder = await _client.GetFromJsonAsync<JsonElement>($"{Nodes}/ladder");

        ladder.GetProperty("inputs").EnumerateArray().Select(pin => pin.GetProperty("name").GetString())
            .ShouldBe(["limitPerOrder", "limitDeposit", "priceDropForBuy", "priceRiseForSell"]);
        ladder.GetProperty("definition").GetProperty("nodes").GetArrayLength().ShouldBeGreaterThan(0);
        (await _client.GetAsync($"{Nodes}/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Built_in_node_cannot_be_changed_or_deleted()
    {
        (await _client.PutAsJsonAsync($"{Nodes}/ladder", Body("x", Signal))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _client.DeleteAsync($"{Nodes}/ladder")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Breaking_change_is_a_conflict_listing_the_usages()
    {
        string id = await CreateSignal();
        await CreateStrategyUsing(id);

        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Nodes}/{id}", Body("Мой сигнал", WithoutOutput));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement usage = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usages").EnumerateArray().ShouldHaveSingleItem();
        (usage.GetProperty("kind").GetString(), usage.GetProperty("name").GetString()).ShouldBe(("strategy", "EMA"));
        usage.GetProperty("errors").TryGetProperty("graph.signal.on", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Used_node_cannot_be_deleted_and_an_unused_one_can()
    {
        string id = await CreateSignal();
        Guid strategy = await CreateStrategyUsing(id);

        (await _client.DeleteAsync($"{Nodes}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await _client.DeleteAsync($"/api/strategies/{strategy}");
        (await _client.DeleteAsync($"{Nodes}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.GetAsync($"{Nodes}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Compatible_change_is_saved()
    {
        string id = await CreateSignal();

        (await _client.PutAsJsonAsync($"{Nodes}/{id}", Body("Сигнал 2", Signal))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetFromJsonAsync<JsonElement>($"{Nodes}/{id}")).GetProperty("title").GetString().ShouldBe("Сигнал 2");
    }

    [Test]
    public async Task Clone_of_a_built_in_node_is_a_custom_node()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync($"{Nodes}/ladder/clone", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        JsonElement clone = await _client.GetFromJsonAsync<JsonElement>($"{Nodes}/{id}");
        (clone.GetProperty("group").GetString(), clone.GetProperty("title").GetString()).ShouldBe(("custom", "Ladder (копия)"));
        (await _client.PostAsJsonAsync($"{Nodes}/nope/clone", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Invalid_node_is_rejected()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Nodes, Body("", """{ "formatVersion": 1, "inputs": [], "outputs": [], "nodes": [ { "id": "m", "kind": "multiply" } ] }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Node_with_graph_errors_is_rejected_with_their_paths()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Nodes, Body("x", """{ "formatVersion": 1, "inputs": [], "outputs": [], "nodes": [ { "id": "m", "kind": "multiply" } ] }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("graph.m.a", out _).ShouldBeTrue();
    }

    private async Task<string> CreateSignal()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Nodes, Body("Мой сигнал", Signal));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().ToString();
    }

    private async Task<Guid> CreateStrategyUsing(string node)
    {
        string definition = $$"""
            {
              "formatVersion": 1, "direction": "long",
              "nodes": [
                { "id": "candle", "kind": "candle" },
                { "id": "signal", "kind": "composite", "ref": "{{node}}" },
                { "id": "buy", "kind": "market-order", "settings": { "side": "buy", "size": "quantity" }, "inputs": { "size": 1 } }
              ],
              "wires": [ { "from": "candle.close", "to": "signal.x" }, { "from": "signal.on", "to": "buy.when" } ]
            }
            """;
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/strategies", Body("EMA", definition));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object Body(string name, string definition)
    {
        return new { name, definition = JsonDocument.Parse(definition).RootElement };
    }
}