using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Signals;
using ProfitFox.Presentation.Contracts.Strategies;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

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
    ///     The strategy without its <c>close-after</c> and the wires of it: a strategy without an exit.
    /// </summary>
    public static void RemoveCloseAfter(JsonObject graph)
    {
        JsonArray nodes = graph["nodes"]!.AsArray();
        nodes.Remove(nodes.Single(node => (string)node!["id"]! == "close-after"));
        JsonArray wires = graph["wires"]!.AsArray();
        foreach (JsonNode? wire in wires.Where(wire => ((string)wire!["from"]!).StartsWith("close-after.", StringComparison.Ordinal)
                                                       || ((string)wire["to"]!).StartsWith("close-after.", StringComparison.Ordinal)).ToList())
        {
            wires.Remove(wire);
        }
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
                  { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
                  { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } },
                  { "id": "position", "kind": "position" }
                ],
                "wires": [
                  { "from": "s.event", "to": "enter.when" },
                  { "from": "enter.order", "to": "position.orders" },
                  { "from": "position.position", "to": "close-after.position" },
                  { "from": "close-after.order", "to": "position.orders" }
                ]
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
        RemoveCloseAfter(graph);

        HttpResponseMessage refused = await Create("Funding Short", graph);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => (error.Code, error.Field, error.Node)), Is.EquivalentTo(new[]
        {
            ("InvalidField", (string?)"name", (string?)null), ("UnknownSignal", null, "s"), ("StrategyExitMissing", null, null)
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

    [Test]
    public async Task The_list_gives_the_signals_and_the_exits_of_the_latest_version()
    {
        Guid signal = await CreateSignal("funding-high");
        await Create("funding-short", Strategy(signal));
        JsonObject changed = JsonNode.Parse(BenchRepository.H0001Text())!["signal"]!.AsObject().DeepClone().AsObject();
        changed["parameters"]![0]!["value"] = 0.001;
        await _api.Http.PutAsJsonAsync("/api/signals/funding-high", new JsonObject { ["graph"] = changed, ["layout"] = new JsonObject() }, Json);

        StrategyListItem item = (await _api.Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!.Single();
        JsonObject raw = JsonNode.Parse(await _api.Http.GetStringAsync("/api/strategies"))!.AsArray().Single()!.AsObject();

        Assert.That(item.Signals, Is.EqualTo(new[] { new StrategySignalItem("funding-high", Version: 1, LatestVersion: 2) }));
        Assert.That(item.Exits, Is.EqualTo(new[] { "close-after" }));
        Assert.That(raw["signals"]!.ToJsonString(), Is.EqualTo("""[{"name":"funding-high","version":1,"latestVersion":2}]"""));
        Assert.That(raw["exits"]!.ToJsonString(), Is.EqualTo("""["close-after"]"""));
        StrategyResponse read = (await _api.Http.GetFromJsonAsync<StrategyResponse>("/api/strategies/funding-short", Json))!;
        Assert.That(read.Signals.Single().Version, Is.EqualTo(1), "the list does not move the reference");
    }

    /// <summary>
    ///     Change add-position-node, task 6.1: spec strategy-constructor «Подъём графа прежнего вида» and «Подъём не
    ///     переписывает версии» on the database.
    /// </summary>
    [Test]
    public async Task A_strategy_of_the_former_form_is_read_raised_and_its_versions_stay_until_it_is_saved()
    {
        Guid signal = await CreateSignal("funding-high");
        await BenchServices.Get<IStrategyRepository>().CreateAsync(
            StrategyDocumentGenerator.Generate(signal, graph: StrategyDocumentGenerator.GenerateGraph(signal, legacy: true)),
            CancellationToken.None);
        string stored = await StoredGraph(version: 1);

        StrategyResponse read = (await _api.Http.GetFromJsonAsync<StrategyResponse>("/api/strategies/funding-short", Json))!;

        JsonObject graph = JsonNode.Parse(read.Graph.GetRawText())!.AsObject();
        Assert.That(graph["nodes"]!.AsArray().Select(node => (string)node!["kind"]!), Does.Contain("position"));
        Assert.That(graph["wires"]!.AsArray().Select(wire => $"{wire!["from"]} -> {wire["to"]}"), Is.SupersetOf(new[]
        {
            "enter.order -> position.orders", "position.position -> close-after.position", "close-after.order -> position.orders"
        }));
        Assert.That((double)graph["nodes"]!.AsArray().Single(node => (string)node!["id"]! == "enter")!["inputs"]!["size"]!, Is.EqualTo(100));
        HttpResponseMessage check = await _api.Http.PostAsJsonAsync("/api/strategies/validate", new JsonObject { ["graph"] = graph.DeepClone() }, Json);
        Assert.That((await check.Content.ReadFromJsonAsync<StrategyValidationResponse>(Json))!.Errors, Is.Empty);
        Assert.That(read.Version, Is.EqualTo(1));
        Assert.That(await Versions(), Is.EqualTo(1), "reading raises nothing in the store");
        Assert.That(await StoredGraph(version: 1), Is.EqualTo(stored).And.Not.Contains("position"));

        HttpResponseMessage saved = await _api.Http.PutAsJsonAsync("/api/strategies/funding-short", new JsonObject { ["graph"] = graph, ["layout"] = new JsonObject() }, Json);

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await saved.Content.ReadFromJsonAsync<StrategyResponse>(Json))!.Version, Is.EqualTo(2), "the raised graph is a new version");
        Assert.That(await Versions(), Is.EqualTo(2));
        Assert.That(await StoredGraph(version: 1), Is.EqualTo(stored), "the former version is not changed");
        Assert.That(await StoredGraph(version: 2), Does.Contain("position.orders"));
    }

    /// <summary>
    ///     Change add-position-node, task 12.3: the answer says that the stored graph was raised on reading, so the editor can
    ///     offer to save it (spec strategy-constructor «Сохранение поднятой стратегии»); once it is saved in the new form the
    ///     answer says so no more.
    /// </summary>
    [Test]
    public async Task A_strategy_read_raised_says_so_until_it_is_saved_in_the_new_form()
    {
        Guid signal = await CreateSignal("funding-high");
        await BenchServices.Get<IStrategyRepository>().CreateAsync(
            StrategyDocumentGenerator.Generate(signal, graph: StrategyDocumentGenerator.GenerateGraph(signal, legacy: true)),
            CancellationToken.None);

        JsonObject read = (await _api.Http.GetFromJsonAsync<JsonObject>("/api/strategies/funding-short", Json))!;

        Assert.That((bool?)read["raised"], Is.True, "the stored graph has no node position: what is read is not what is stored");

        HttpResponseMessage saved = await _api.Http.PutAsJsonAsync("/api/strategies/funding-short", new JsonObject { ["graph"] = read["graph"]!.DeepClone(), ["layout"] = new JsonObject() }, Json);

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((bool?)(await saved.Content.ReadFromJsonAsync<JsonObject>(Json))!["raised"], Is.False);
        Assert.That((bool?)(await _api.Http.GetFromJsonAsync<JsonObject>("/api/strategies/funding-short", Json))!["raised"], Is.False, "the new version is stored in the new form");
    }

    [Test]
    public async Task A_strategy_stored_in_the_new_form_is_not_raised()
    {
        Guid signal = await CreateSignal("funding-high");
        await BenchServices.Get<IStrategyRepository>().CreateAsync(StrategyDocumentGenerator.Generate(signal), CancellationToken.None);

        JsonObject read = (await _api.Http.GetFromJsonAsync<JsonObject>("/api/strategies/funding-short", Json))!;

        Assert.That((bool?)read["raised"], Is.False);
    }

    [Test]
    public async Task A_graph_of_the_former_form_sent_to_the_check_is_not_raised()
    {
        Guid signal = await CreateSignal("funding-high");
        string former = ProfitFox.Domain.Services.Graph.StrategyGraph.Write(StrategyDocumentGenerator.GenerateGraph(signal, legacy: true));

        HttpResponseMessage check = await _api.Http.PostAsJsonAsync("/api/strategies/validate", new JsonObject { ["graph"] = JsonNode.Parse(former) }, Json);

        StrategyValidationResponse response = (await check.Content.ReadFromJsonAsync<StrategyValidationResponse>(Json))!;
        Assert.That(response.Errors.Select(error => error.Code), Does.Contain("StrategyPositionMissing"));
        Assert.That(response.Errors.Single(error => error.Code == "StrategyPositionMissing").Reason, Does.Contain("узел позиции"));
    }

    private static async Task<int> Versions()
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM strategy_versions");
    }

    private static async Task<string> StoredGraph(int version)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT graph::text FROM strategy_versions WHERE version = @Version", new { Version = version }) ?? "";
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
