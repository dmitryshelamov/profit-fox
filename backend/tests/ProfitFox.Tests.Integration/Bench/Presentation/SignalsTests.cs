using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Npgsql;
using ProfitFox.Bench.Presentation.Errors;
using ProfitFox.Bench.Presentation.Signals;
using ProfitFox.Tests.Integration.Bench.Database;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Presentation;

/// <summary>
///     Spec signal-constructor end to end (tasks 3.2 and 3.3): the signals of the bench database through the API and the
///     check of a graph without history.
/// </summary>
[NonParallelizable]
public sealed class SignalsTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private BenchPresentationServer _api = null!;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = await BenchPresentationServer.Start(_market);
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
