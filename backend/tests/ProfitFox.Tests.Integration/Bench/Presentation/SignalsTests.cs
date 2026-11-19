using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProfitFox.Bench.Hypotheses;
using ProfitFox.Bench.Presentation.Errors;
using ProfitFox.Bench.Presentation.Signals;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Presentation;

/// <summary>
///     Spec signal-constructor end to end (tasks 3.2 and 3.3): the signals of a temporary <c>bench/signals</c> and the
///     check of a graph without history.
/// </summary>
[NonParallelizable]
public sealed class SignalsTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private string _bench = "";
    private string _signals = "";
    private MarketDataServer _market = null!;
    private BenchPresentationServer _api = null!;

    [SetUp]
    public async Task Start()
    {
        _bench = Path.Combine(Path.GetTempPath(), $"profit-fox-signals-{Guid.NewGuid():N}", "bench");
        _signals = Path.Combine(_bench, "signals");
        Directory.CreateDirectory(_bench);
        _market = await MarketDataServer.Start();
        _api = await BenchPresentationServer.Start(_market, _bench);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
        Directory.Delete(Path.GetDirectoryName(_bench)!, recursive: true);
    }

    [Test]
    public async Task A_created_signal_is_a_file_with_its_name_and_the_graph_of_H0001()
    {
        HttpResponseMessage created = await Create("funding-high", Signal(), new JsonObject { ["rate"] = new JsonObject { ["x"] = 0, ["y"] = 120 } });

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        Assert.That(created.Headers.Location!.ToString(), Does.EndWith("/api/signals/funding-high"));
        JsonNode file = JsonNode.Parse(File.ReadAllText(Path.Combine(_signals, "funding-high.json")))!;
        Assert.That(file["name"]!.GetValue<string>(), Is.EqualTo("funding-high"));
        Assert.That(JsonNode.DeepEquals(file["graph"], Signal()), Is.True);
        SignalResponse read = (await _api.Http.GetFromJsonAsync<SignalResponse>("/api/signals/funding-high", Json))!;
        Assert.That(read.Layout["rate"].Y, Is.EqualTo(120));
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(read.Graph.GetRawText()), Signal()), Is.True);
    }

    [Test]
    public async Task The_list_is_in_name_order_and_keeps_a_broken_file()
    {
        await Create("funding-high", Signal());
        await Create("candle-drop", Signal());
        File.WriteAllText(Path.Combine(_signals, "broken.json"), """{ "name": "broken", "layout": {} }""");

        SignalListItem[] list = (await _api.Http.GetFromJsonAsync<SignalListItem[]>("/api/signals", Json))!;

        Assert.That(list.Select(item => item.Name), Is.EqualTo(new[] { "broken", "candle-drop", "funding-high" }));
        Assert.That(list[0].Error, Does.Contain("graph"));
        HttpResponseMessage broken = await _api.Http.GetAsync("/api/signals/broken");
        Assert.That(broken.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await broken.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Single().Reason, Does.Contain("graph"));
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
        Assert.That(Directory.Exists(_signals), Is.False);
    }

    [Test]
    public async Task A_replace_with_errors_leaves_the_file()
    {
        await Create("funding-high", Signal());
        string before = File.ReadAllText(Path.Combine(_signals, "funding-high.json"));
        JsonObject signal = Signal();
        signal["wires"]!.AsArray().RemoveAt(1);

        HttpResponseMessage refused = await _api.Http.PutAsJsonAsync("/api/signals/funding-high", new JsonObject { ["graph"] = signal, ["layout"] = new JsonObject() }, Json);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(File.ReadAllText(Path.Combine(_signals, "funding-high.json")), Is.EqualTo(before));
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
        Assert.That(Directory.GetFiles(_signals).Select(Path.GetFileName).Order(), Is.EqualTo(new[] { "funding-high.json", "price-drop.json" }));
    }

    [Test]
    public async Task Rename_and_delete()
    {
        await Create("price-drop", Signal());

        HttpResponseMessage renamed = await _api.Http.PostAsJsonAsync("/api/signals/price-drop/rename", new { to = "price-fall" }, Json);
        HttpResponseMessage deleted = await _api.Http.DeleteAsync("/api/signals/price-fall");

        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await renamed.Content.ReadFromJsonAsync<SignalResponse>(Json))!.Name, Is.EqualTo("price-fall"));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(Directory.GetFiles(_signals), Is.Empty);
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

    private static JsonObject Signal()
    {
        DirectoryInfo? folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        string file = File.ReadAllText(Path.Combine(folder!.FullName, "bench", "hypotheses", "H0001-funding-short-4h", HypothesisReader.FileName));
        return JsonNode.Parse(file)!["signal"]!.AsObject().DeepClone().AsObject();
    }
}
