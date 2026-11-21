using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Signals;
using ProfitFox.Presentation.Contracts.Strategies;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Spec strategy-constructor end to end (change add-strategy-constructor, task 4.4): the strategies of the bench database
///     through the API and the check of a graph as a strategy without history.
/// </summary>
[NonParallelizable]
public sealed class StrategiesTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
    }

    /// <summary>
    ///     <c>funding-short</c>: the node <c>s</c> on <paramref name="version" /> of <paramref name="signal" />, a short
    ///     <c>enter</c> on its event and a <c>close-after</c> of <paramref name="after" />.
    /// </summary>
    public static JsonObject Strategy(Guid signal, int version = 1, string after = "4h")
    {
        return JsonNode.Parse(
            $$"""
              {
                "formatVersion": 1,
                "nodes": [
                  { "id": "s", "kind": "signal", "settings": { "signal": "{{signal}}", "version": "{{version}}" } },
                  { "id": "enter", "kind": "enter", "settings": { "side": "short" } },
                  { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }
                ],
                "wires": [{ "from": "s.event", "to": "enter.when" }]
              }
              """)!.AsObject();
    }

    [Test]
    public async Task A_saved_strategy()
    {
        Guid signal = await CreateSignal("funding-high");

        HttpResponseMessage created = await Create("funding-short", Strategy(signal));

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        Assert.That(created.Headers.Location!.ToString(), Does.EndWith("/api/strategies/funding-short"));
        StrategyResponse read = (await _api.Http.GetFromJsonAsync<StrategyResponse>("/api/strategies/funding-short", Json))!;
        Assert.That((read.Name, read.Version), Is.EqualTo(("funding-short", 1)));
        Assert.That(read.Graph.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("kind").GetString()), Has.Member("signal"));
        SignalUseResponse use = read.Signals.Single();
        Assert.That((use.Node, use.Signal, use.Name, use.Version, use.LatestVersion), Is.EqualTo(("s", signal, (string?)"funding-high", 1, (int?)1)));
        SignalParameterResponse threshold = use.Parameters.Single();
        Assert.That((threshold.Name, threshold.Type, threshold.Value.GetDecimal()), Is.EqualTo(("threshold", "number", 0.0005m)));
    }

    [Test]
    public async Task The_list()
    {
        Guid signal = await CreateSignal("funding-high");
        await Create("funding-short", Strategy(signal));
        await _api.Http.PutAsJsonAsync("/api/strategies/funding-short", new JsonObject { ["graph"] = Strategy(signal, after: "8h"), ["layout"] = new JsonObject() }, Json);
        await Create("price-drop-long", Strategy(signal));

        StrategyListItem[] list = (await _api.Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!;

        Assert.That(list.Select(item => (item.Name, item.Version, item.Error)), Is.EqualTo(new[] { ("funding-short", 2, (string?)null), ("price-drop-long", 1, null) }));
    }

    [Test]
    public async Task Deleting()
    {
        Guid signal = await CreateSignal("funding-high");
        await Create("funding-short", Strategy(signal));

        HttpResponseMessage deleted = await _api.Http.DeleteAsync("/api/strategies/funding-short");

        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await _api.Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!, Is.Empty);
        Assert.That((await _api.Http.GetAsync("/api/strategies/funding-short")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.Http.DeleteAsync("/api/strategies/funding-short")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_graph_with_errors_and_a_bad_name_are_refused_with_every_error()
    {
        Guid signal = await CreateSignal("funding-high");
        JsonObject graph = Strategy(signal, version: 7);
        graph["nodes"]!.AsArray().RemoveAt(2);

        HttpResponseMessage refused = await Create("Funding Short", graph);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => (error.Code, error.Field, error.Node)), Is.EquivalentTo(new[]
        {
            ("InvalidField", (string?)"name", (string?)null), ("UnknownSignal", null, "s"), ("StrategyCloseAfterMissing", null, null)
        }));
        Assert.That((await _api.Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!, Is.Empty);
    }

    [Test]
    public async Task A_taken_name_and_a_rename_that_keeps_the_versions()
    {
        Guid signal = await CreateSignal("funding-high");
        await Create("funding-short", Strategy(signal));
        await Create("price-drop-long", Strategy(signal));
        await _api.Http.PutAsJsonAsync("/api/strategies/price-drop-long", new JsonObject { ["graph"] = Strategy(signal, after: "8h"), ["layout"] = new JsonObject() }, Json);

        HttpResponseMessage taken = await _api.Http.PostAsJsonAsync("/api/strategies/price-drop-long/rename", new { to = "funding-short" }, Json);
        HttpResponseMessage renamed = await _api.Http.PostAsJsonAsync("/api/strategies/price-drop-long/rename", new { to = "price-drop-long-8h" }, Json);

        Assert.That(taken.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await renamed.Content.ReadFromJsonAsync<StrategyResponse>(Json))!.Version, Is.EqualTo(2));
        Assert.That((await Create("funding-short", Strategy(signal))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task A_check_gives_errors_and_the_signal_nodes_and_reads_no_history()
    {
        Guid signal = await CreateSignal("funding-high");
        JsonObject graph = Strategy(signal);
        graph["nodes"]![1]!.AsObject().Remove("settings");

        HttpResponseMessage response = await _api.Http.PostAsJsonAsync("/api/strategies/validate", new JsonObject { ["graph"] = graph }, Json);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        StrategyValidationResponse checkedGraph = (await response.Content.ReadFromJsonAsync<StrategyValidationResponse>(Json))!;
        Assert.That(checkedGraph.Errors.Select(error => (error.Code, error.Node, error.Setting)), Is.EqualTo(new[] { ("MissingSetting", (string?)"enter", (string?)"side") }));
        Assert.That(checkedGraph.Signals.Single().Name, Is.EqualTo("funding-high"));
        Assert.That(_market.Data.CandleRequests, Is.Empty);
        Assert.That(_market.Data.FundingRequests, Is.Empty);
    }

    private async Task<Guid> CreateSignal(string name)
    {
        JsonObject graph = JsonNode.Parse(BenchRepository.H0001Text())!["signal"]!.AsObject().DeepClone().AsObject();
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = name, ["graph"] = graph, ["layout"] = new JsonObject() }, Json);
        return (await created.Content.ReadFromJsonAsync<SignalResponse>(Json))!.Id;
    }

    private Task<HttpResponseMessage> Create(string name, JsonObject graph)
    {
        return _api.Http.PostAsJsonAsync("/api/strategies", new JsonObject { ["name"] = name, ["graph"] = graph, ["layout"] = new JsonObject() }, Json);
    }
}
