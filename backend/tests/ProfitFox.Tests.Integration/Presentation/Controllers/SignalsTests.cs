using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Signals;
using ProfitFox.Presentation.Contracts.Strategies;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Spec signal-constructor end to end (tasks 3.2 and 3.3): the signals of the bench database through the API and the
///     check of a graph without history.
/// </summary>
[NonParallelizable]
public sealed class SignalsTests
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

    [Test]
    public async Task A_created_signal_keeps_its_name_and_the_graph_of_H0001_as_version_1()
    {
        HttpResponseMessage created = await Create("funding-high", Signal(), new JsonObject { ["rate"] = new JsonObject { ["x"] = 0, ["y"] = 120 } });

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        Assert.That(created.Headers.Location!.ToString(), Does.EndWith("/api/signals/funding-high"));
        SignalResponse read = (await _api.Http.GetFromJsonAsync<SignalResponse>("/api/signals/funding-high", Json))!;
        Assert.That((read.Name, read.Version), Is.EqualTo(("funding-high", 1)));
        Assert.That(read.Layout["rate"].Y, Is.EqualTo(120));
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(read.Graph.GetRawText()), Signal()), Is.True);
    }

    [Test]
    public async Task The_list_is_in_name_order_with_the_latest_versions()
    {
        await Create("funding-high", Signal());
        await Create("candle-drop", Signal());
        JsonObject higher = Signal();
        higher["parameters"]![0]!["value"] = 0.001;
        await _api.Http.PutAsJsonAsync("/api/signals/funding-high", new JsonObject { ["graph"] = higher, ["layout"] = new JsonObject() }, Json);

        SignalListItem[] list = (await _api.Http.GetFromJsonAsync<SignalListItem[]>("/api/signals", Json))!;

        Assert.That(list.Select(item => (item.Name, item.Version, item.Error)), Is.EqualTo(new[] { ("candle-drop", 1, (string?)null), ("funding-high", 2, null) }));
    }

    [Test]
    public async Task A_graph_with_errors_or_a_bad_name_is_refused_with_every_error()
    {
        JsonObject signal = Signal();
        signal["wires"]!.AsArray().RemoveAt(1);

        HttpResponseMessage refused = await Create("Funding High", signal);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => (error.Field, error.Node, error.Pin)), Is.EquivalentTo(new[] { ("name", (string?)null, (string?)null), (null, "high", "b") }));
        Assert.That(await Count("signals"), Is.Zero);
    }

    [Test]
    public async Task A_replace_with_errors_leaves_the_signal_and_its_version()
    {
        await Create("funding-high", Signal());
        string before = await _api.Http.GetStringAsync("/api/signals/funding-high");
        JsonObject signal = Signal();
        signal["wires"]!.AsArray().RemoveAt(1);

        HttpResponseMessage refused = await _api.Http.PutAsJsonAsync("/api/signals/funding-high", new JsonObject { ["graph"] = signal, ["layout"] = new JsonObject() }, Json);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await _api.Http.GetStringAsync("/api/signals/funding-high"), Is.EqualTo(before));
        Assert.That(await Count("signal_versions"), Is.EqualTo(1));
        HttpResponseMessage missing = await _api.Http.PutAsJsonAsync("/api/signals/nothing", new JsonObject { ["graph"] = Signal() }, Json);
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_taken_name_is_refused_on_create_and_on_rename()
    {
        await Create("funding-high", Signal());
        await Create("price-drop", Signal());

        HttpResponseMessage again = await Create("funding-high", Signal());
        HttpResponseMessage rename = await _api.Http.PostAsJsonAsync("/api/signals/price-drop/rename", new { to = "funding-high" }, Json);

        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await rename.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Single().Field, Is.EqualTo("name"));
        Assert.That(await Count("signals"), Is.EqualTo(2));
    }

    [Test]
    public async Task Rename_keeps_the_versions_and_delete_removes_them()
    {
        await Create("price-drop", Signal());
        JsonObject higher = Signal();
        higher["parameters"]![0]!["value"] = 0.001;
        await _api.Http.PutAsJsonAsync("/api/signals/price-drop", new JsonObject { ["graph"] = higher, ["layout"] = new JsonObject() }, Json);

        HttpResponseMessage renamed = await _api.Http.PostAsJsonAsync("/api/signals/price-drop/rename", new { to = "price-fall" }, Json);
        int versions = await Count("signal_versions");
        HttpResponseMessage deleted = await _api.Http.DeleteAsync("/api/signals/price-fall");

        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        SignalResponse signal = (await renamed.Content.ReadFromJsonAsync<SignalResponse>(Json))!;
        Assert.That((signal.Name, signal.Version), Is.EqualTo(("price-fall", 2)));
        Assert.That(versions, Is.EqualTo(2));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await Count("signals"), await Count("signal_versions")), Is.EqualTo((0, 0)));
        Assert.That((await _api.Http.DeleteAsync("/api/signals/price-fall")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_check_names_a_wire_to_a_missing_node_and_reads_no_history()
    {
        JsonObject signal = Signal();
        signal["wires"]![0]!["to"] = "hgh.a";

        ValidationResponse checkedGraph = await Validate(signal);

        Assert.That(checkedGraph.Errors, Has.Some.Matches<RequestError>(error => error.Wire == new RequestWire("rate.value", "hgh.a")));
        Assert.That(_market.Data.CandleRequests, Is.Empty);
        Assert.That(_market.Data.FundingRequests, Is.Empty);
    }

    [Test]
    public async Task The_graph_of_H0001_checks_without_errors()
    {
        ValidationResponse checkedGraph = await Validate(Signal());

        Assert.That(checkedGraph.Errors, Is.Empty);
    }

    [Test]
    public async Task The_id_after_a_rename()
    {
        SignalResponse created = (await (await Create("funding-high", Signal())).Content.ReadFromJsonAsync<SignalResponse>(Json))!;
        JsonObject higher = Signal();
        higher["parameters"]![0]!["value"] = 0.001;
        await _api.Http.PutAsJsonAsync("/api/signals/funding-high", new JsonObject { ["graph"] = higher, ["layout"] = new JsonObject() }, Json);

        HttpResponseMessage renamed = await _api.Http.PostAsJsonAsync("/api/signals/funding-high/rename", new { to = "funding-spike" }, Json);

        Assert.That((await renamed.Content.ReadFromJsonAsync<SignalResponse>(Json))!.Id, Is.EqualTo(created.Id));
        Assert.That((await _api.Http.GetFromJsonAsync<SignalResponse>("/api/signals/funding-spike", Json))!.Id, Is.EqualTo(created.Id));
        Assert.That((await _api.Http.GetFromJsonAsync<SignalListItem[]>("/api/signals", Json))!.Single().Id, Is.EqualTo(created.Id));
        Assert.That(created.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task The_id_of_a_copy()
    {
        SignalResponse original = (await (await Create("funding-high", Signal())).Content.ReadFromJsonAsync<SignalResponse>(Json))!;

        SignalResponse copy = (await (await Create("funding-copy", JsonNode.Parse(original.Graph.GetRawText())!.AsObject())).Content.ReadFromJsonAsync<SignalResponse>(Json))!;

        Assert.That(copy.Id, Is.Not.EqualTo(original.Id));
    }

    [Test]
    public async Task Renaming_a_signal_in_use()
    {
        SignalResponse signal = (await (await Create("funding-high", Signal())).Content.ReadFromJsonAsync<SignalResponse>(Json))!;
        await CreateStrategy("funding-short", signal.Id);

        HttpResponseMessage renamed = await _api.Http.PostAsJsonAsync("/api/signals/funding-high/rename", new { to = "funding-spike" }, Json);
        HttpResponseMessage strategy = await _api.Http.GetAsync("/api/strategies/funding-short");

        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(strategy.StatusCode, Is.EqualTo(HttpStatusCode.OK), await strategy.Content.ReadAsStringAsync());
        StrategyResponse read = (await strategy.Content.ReadFromJsonAsync<StrategyResponse>(Json))!;
        Assert.That((read.Version, read.Signals.Single().Name), Is.EqualTo((1, "funding-spike")));
        StrategyValidationResponse checkedGraph = await ValidateStrategy(JsonNode.Parse(read.Graph.GetRawText())!.AsObject());
        Assert.That(checkedGraph.Errors, Is.Empty);
    }

    [Test]
    public async Task Deleting_a_signal_in_use()
    {
        SignalResponse signal = (await (await Create("funding-high", Signal())).Content.ReadFromJsonAsync<SignalResponse>(Json))!;
        await CreateStrategy("funding-short", signal.Id);

        HttpResponseMessage deleted = await _api.Http.DeleteAsync("/api/signals/funding-high");

        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        SignalInUseResponse refusal = (await deleted.Content.ReadFromJsonAsync<SignalInUseResponse>(Json))!;
        Assert.That((refusal.Errors.Single().Code, refusal.Strategies), Is.EqualTo(("SignalInUse", new[] { "funding-short" })).Using<(string, IReadOnlyList<string>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That((await _api.Http.GetAsync("/api/signals/funding-high")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await _api.Http.DeleteAsync("/api/strategies/funding-short")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await _api.Http.DeleteAsync("/api/signals/funding-high")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "deleting the strategy lifts the refusal");
    }

    [Test]
    public async Task The_list_names_the_strategies_of_each_signal()
    {
        SignalResponse used = (await (await Create("funding-high", Signal())).Content.ReadFromJsonAsync<SignalResponse>(Json))!;
        await Create("candle-drop", Signal());
        await CreateStrategy("zeta", used.Id);
        await CreateStrategy("alpha", used.Id);

        SignalListItem[] list = (await _api.Http.GetFromJsonAsync<SignalListItem[]>("/api/signals", Json))!;
        JsonArray raw = JsonNode.Parse(await _api.Http.GetStringAsync("/api/signals"))!.AsArray();

        Assert.That(list.Select(item => item.Name), Is.EqualTo(new[] { "candle-drop", "funding-high" }));
        Assert.That(list[0].Strategies, Is.Empty);
        Assert.That(list[1].Strategies, Is.EqualTo(new[] { "alpha", "zeta" }));
        Assert.That(raw[0]!["strategies"]!.GetValueKind(), Is.EqualTo(JsonValueKind.Array), "always an array, also when it is empty");
    }

    private async Task CreateStrategy(string name, Guid signal)
    {
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync(
            "/api/strategies",
            new JsonObject { ["name"] = name, ["graph"] = StrategiesTests.Strategy(signal), ["layout"] = new JsonObject() },
            Json);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
    }

    private async Task<StrategyValidationResponse> ValidateStrategy(JsonObject graph)
    {
        HttpResponseMessage response = await _api.Http.PostAsJsonAsync("/api/strategies/validate", new JsonObject { ["graph"] = graph }, Json);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<StrategyValidationResponse>(Json))!;
    }

    private async Task<ValidationResponse> Validate(JsonObject graph)
    {
        HttpResponseMessage response = await _api.Http.PostAsJsonAsync("/api/signals/validate", new JsonObject { ["graph"] = graph }, Json);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<ValidationResponse>(Json))!;
    }

    private Task<HttpResponseMessage> Create(string name, JsonObject graph, JsonObject? layout = null)
    {
        return _api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = name, ["graph"] = graph, ["layout"] = layout ?? new JsonObject() }, Json);
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }

    private static JsonObject Signal()
    {
        return JsonNode.Parse(BenchRepository.H0001Text())!["signal"]!.AsObject().DeepClone().AsObject();
    }
}
