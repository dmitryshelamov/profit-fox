using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

using ProfitFox.Application.Commands.Signals;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals;

namespace ProfitFox.Tests.Integration.Database;

/// <summary>
///     Spec signal-constructor on the database: the versions of a signal (a graph that differs by content makes the next one,
///     a layout or a description does not), names, rename, copy and delete.
/// </summary>
public sealed class SignalStoreTests
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];
    private readonly SignalLibrary _library = new SignalLibrary(new SignalStore(BenchDatabase.DataSource));
    private readonly CancellationToken _none = CancellationToken.None;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task A_saved_signal_is_version_1_and_reads_as_the_signal_of_H0001()
    {
        SignalDocument created = await _library.Create("funding-high", "Фандинг впервые не ниже порога", Graph(), NoLayout, _none);

        SignalDocument read = await _library.Read("funding-high", _none);

        Assert.That((created.Version, read.Version, read.Name, read.Description), Is.EqualTo((1, 1, "funding-high", "Фандинг впервые не ниже порога")));
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(StrategyGraph.Write(read.Graph)), GraphNode()), Is.True);
        Assert.That(read.Graph.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "p", "rate", "high", "first", "out" }));
    }

    [Test]
    public async Task A_changed_threshold_is_version_2_and_the_graph_of_version_1_stays()
    {
        await _library.Create("funding-high", null, Graph(), NoLayout, _none);

        SignalDocument replaced = await _library.Replace("funding-high", null, Graph(threshold: 0.001), NoLayout, _none);

        Assert.That(replaced.Version, Is.EqualTo(2));
        Assert.That((await _library.Read("funding-high", _none)).Version, Is.EqualTo(2));
        Assert.That(await ThresholdOf(version: 1), Is.EqualTo(0.0005));
        Assert.That(await ThresholdOf(version: 2), Is.EqualTo(0.001));
    }

    [Test]
    public async Task Moving_a_node_changes_only_the_layout_and_adds_no_version()
    {
        await _library.Create("funding-high", null, Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(0, 120) }, _none);
        await _library.Replace("funding-high", null, Graph(threshold: 0.001), new Dictionary<string, NodePosition> { ["rate"] = new(0, 120) }, _none);
        string graphBefore = await GraphText("funding-high");

        SignalDocument moved = await _library.Replace("funding-high", null, Graph(threshold: 0.001), new Dictionary<string, NodePosition> { ["rate"] = new(40, 200.5) }, _none);

        Assert.That(moved.Version, Is.EqualTo(2));
        Assert.That(await GraphText("funding-high"), Is.EqualTo(graphBefore));
        Assert.That(await Count("signal_versions"), Is.EqualTo(2));
        Assert.That((await _library.Read("funding-high", _none)).Layout["rate"], Is.EqualTo(new NodePosition(40, 200.5)));
    }

    [Test]
    public async Task The_same_graph_in_another_key_order_and_spacing_adds_no_version()
    {
        await _library.Create("funding-high", null, Graph(), NoLayout, _none);
        JsonElement reordered = Element(Reversed(GraphNode()), indented: true);

        SignalDocument replaced = await _library.Replace("funding-high", "новое описание", reordered, NoLayout, _none);

        Assert.That(replaced.Version, Is.EqualTo(1));
        Assert.That(await Count("signal_versions"), Is.EqualTo(1));
        Assert.That((await _library.Read("funding-high", _none)).Description, Is.EqualTo("новое описание"));
    }

    [TestCase("Funding High")]
    [TestCase("funding--high")]
    [TestCase("../funding")]
    [TestCase("")]
    public async Task A_name_against_the_rule_is_refused_and_nothing_is_written(string name)
    {
        SignalLibraryException error = Assert.ThrowsAsync<SignalLibraryException>(() => _library.Create(name, null, Graph(), NoLayout, _none))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(await Count("signals"), Is.Zero);
    }

    [Test]
    public async Task A_graph_with_an_error_is_refused_and_the_saved_signal_and_its_version_stay()
    {
        await _library.Create("funding-high", null, Graph(), NoLayout, _none);
        JsonNode broken = GraphNode();
        broken["wires"]!.AsArray().RemoveAt(1);

        SignalLibraryException error = Assert.ThrowsAsync<SignalLibraryException>(() => _library.Replace("funding-high", null, Element(broken), NoLayout, _none))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => (item.Node, item.Pin)), Is.EqualTo(new[] { ((string?)"high", (string?)"b") }));
        Assert.That((await _library.Read("funding-high", _none)).Version, Is.EqualTo(1));
        Assert.That(await Count("signal_versions"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_taken_name_is_refused_on_create_and_rename_and_both_signals_stay()
    {
        await _library.Create("funding-high", null, Graph(), NoLayout, _none);
        await _library.Create("price-drop", "второй", Graph(), NoLayout, _none);

        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Create("funding-high", null, Graph(), NoLayout, _none))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Rename("price-drop", "funding-high", _none))!.Problem, Is.EqualTo(SignalProblem.NameTaken));

        Assert.That((await _library.List(_none)).Select(entry => (entry.Name, entry.Description)), Is.EqualTo(new[] { ("funding-high", (string?)null), ("price-drop", "второй") }));
    }

    [Test]
    public async Task Two_saves_of_one_new_name_together_leave_one_signal_and_one_refusal()
    {
        Task<SignalDocument> first = Task.Run(() => _library.Create("funding-high", null, Graph(), NoLayout, _none));
        Task<SignalDocument> second = Task.Run(() => _library.Create("funding-high", null, Graph(threshold: 0.001), NoLayout, _none));

        await Task.WhenAny(first, second);
        await Task.WhenAll(first.ContinueWith(_ => { }), second.ContinueWith(_ => { }));

        Assert.That(new[] { first, second }.Count(task => task.IsCompletedSuccessfully), Is.EqualTo(1));
        Assert.That(new[] { first, second }.Single(task => task.IsFaulted).Exception!.InnerException, Is.InstanceOf<SignalLibraryException>().With.Property(nameof(SignalLibraryException.Problem)).EqualTo(SignalProblem.NameTaken));
        Assert.That(await Count("signals"), Is.EqualTo(1));
    }

    [Test]
    public async Task Saves_of_one_signal_together_take_the_next_versions_one_by_one()
    {
        await _library.Create("funding-high", null, Graph(), NoLayout, _none);

        SignalDocument[] saved = await Task.WhenAll(Enumerable.Range(1, 5).Select(number => Task.Run(() => _library.Replace("funding-high", null, Graph(threshold: number / 1000.0), NoLayout, _none))));

        Assert.That(saved.Select(signal => signal.Version).Order(), Is.EqualTo(new[] { 2, 3, 4, 5, 6 }));
        Assert.That(await Count("signal_versions"), Is.EqualTo(6));
    }

    [Test]
    public async Task Rename_keeps_the_versions_and_a_copy_starts_at_version_1()
    {
        await _library.Create("price-drop", "описание", Graph(), NoLayout, _none);
        await _library.Replace("price-drop", "описание", Graph(threshold: 0.001), NoLayout, _none);

        await _library.Rename("price-drop", "price-fall", _none);
        SignalDocument renamed = await _library.Read("price-fall", _none);
        SignalDocument copy = await _library.Create("price-copy", renamed.Description, Element(JsonNode.Parse(StrategyGraph.Write(renamed.Graph))!), renamed.Layout, _none);

        Assert.That((renamed.Name, renamed.Version, renamed.Description), Is.EqualTo(("price-fall", 2, "описание")));
        Assert.That(copy.Version, Is.EqualTo(1));
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Read("price-drop", _none))!.Problem, Is.EqualTo(SignalProblem.NotFound));
        Assert.That(await Count("signal_versions"), Is.EqualTo(3));
    }

    [Test]
    public async Task Delete_removes_the_signal_with_its_versions()
    {
        await _library.Create("price-drop", null, Graph(), NoLayout, _none);
        await _library.Replace("price-drop", null, Graph(threshold: 0.001), NoLayout, _none);

        await _library.Delete("price-drop", _none);

        Assert.That((await _library.List(_none)), Is.Empty);
        Assert.That(await Count("signal_versions"), Is.Zero);
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Delete("price-drop", _none))!.Problem, Is.EqualTo(SignalProblem.NotFound));
    }

    [Test]
    public async Task The_list_is_in_name_order_with_the_latest_versions()
    {
        await _library.Create("funding-high", null, Graph(), NoLayout, _none);
        await _library.Create("candle-drop", "свеча", Graph(), NoLayout, _none);
        await _library.Replace("funding-high", null, Graph(threshold: 0.001), NoLayout, _none);

        IReadOnlyList<SignalEntry> list = await _library.List(_none);

        Assert.That(list, Is.EqualTo(new[] { new SignalEntry("candle-drop", "свеча", 1, Error: null), new SignalEntry("funding-high", null, 2, Error: null) }));
    }

    [Test]
    public async Task The_layout_keeps_only_nodes_of_the_graph()
    {
        await _library.Create("funding-high", null, Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(0, 0), ["gone"] = new(1, 1) }, _none);

        Assert.That((await _library.Read("funding-high", _none)).Layout.Keys, Is.EqualTo(new[] { "rate" }));
    }

    private static JsonNode GraphNode(double threshold = 0.0005)
    {
        JsonNode graph = JsonNode.Parse(BenchRepository.H0001Text())!["signal"]!.DeepClone();
        graph["parameters"]![0]!["value"] = threshold;
        return graph;
    }

    private static JsonElement Graph(double threshold = 0.0005)
    {
        return Element(GraphNode(threshold));
    }

    private static JsonElement Element(JsonNode node, bool indented = false)
    {
        return JsonDocument.Parse(node.ToJsonString(new JsonSerializerOptions { WriteIndented = indented })).RootElement.Clone();
    }

    // The same content with the properties of every object in the other order.
    private static JsonNode Reversed(JsonNode node)
    {
        switch (node)
        {
            case JsonObject properties:
                var reversed = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> property in properties.Reverse())
                {
                    reversed[property.Key] = property.Value is null ? null : Reversed(property.Value);
                }

                return reversed;
            case JsonArray items:
                return new JsonArray([.. items.Select(item => item is null ? null : Reversed(item))]);
            default:
                return node.DeepClone();
        }
    }

    private static async Task<double> ThresholdOf(int version)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<double>(
            "SELECT (graph -> 'parameters' -> 0 ->> 'value')::double precision FROM signal_versions WHERE version = @Version",
            new { Version = version });
    }

    private static async Task<string> GraphText(string name)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>(
            "SELECT v.graph::text FROM signal_versions v JOIN signals s ON s.id = v.signal_id AND s.version = v.version WHERE s.name = @Name",
            new { Name = name }) ?? "";
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }
}
