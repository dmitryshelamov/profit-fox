using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Commands.Signals;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Tests.Unit.Domain.Services.Hypotheses;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     Spec signal-constructor: the name of a signal, saving only a graph without errors, and the operations of the library
///     (task 2.1), on a store in memory. The versions and the database are tested with the store in the integration tests.
/// </summary>
public sealed class SignalLibraryTests
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];
    private readonly CancellationToken _none = CancellationToken.None;

    private SignalLibrary _library = null!;

    [SetUp]
    public void Create()
    {
        _library = new SignalLibrary(new MemorySignalStore());
    }

    [TestCase("Funding High")]
    [TestCase("Funding")]
    [TestCase("funding--high")]
    [TestCase("-funding")]
    [TestCase("../funding")]
    [TestCase("")]
    public async Task A_name_against_the_rule_is_refused_and_nothing_is_written(string name)
    {
        SignalLibraryException error = Assert.ThrowsAsync<SignalLibraryException>(() => _library.Create(name, null, H0001Graph(), NoLayout, _none))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => item.Field), Has.Member("name"));
        Assert.That(await _library.List(_none), Is.Empty);
    }

    [Test]
    public void A_name_of_65_characters_is_refused()
    {
        Assert.That(SignalNames.IsValid(new string('a', 64)), Is.True);
        Assert.That(SignalNames.IsValid(new string('a', 65)), Is.False);
    }

    [Test]
    public async Task A_graph_with_an_error_is_refused_and_the_saved_signal_stays()
    {
        await _library.Create("funding-high", null, H0001Graph(), NoLayout, _none);
        JsonNode broken = H0001GraphNode();
        broken["wires"]!.AsArray().RemoveAt(1);

        SignalLibraryException error = Assert.ThrowsAsync<SignalLibraryException>(() => _library.Replace("funding-high", null, Element(broken), NoLayout, _none))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => (item.Code, item.Node, item.Pin)), Is.EqualTo(new[] { (GraphErrorCode.MissingInput, (string?)"high", (string?)"b") }));
        Assert.That((await _library.Read("funding-high", _none)).Graph.Nodes, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task The_layout_keeps_only_nodes_of_the_graph()
    {
        await _library.Create("funding-high", null, H0001Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(0, 0), ["gone"] = new(1, 1) }, _none);

        Assert.That((await _library.Read("funding-high", _none)).Layout.Keys, Is.EqualTo(new[] { "rate" }));
    }

    [Test]
    public async Task A_taken_name_is_refused_on_create_and_rename_and_both_signals_stay()
    {
        await _library.Create("funding-high", null, H0001Graph(), NoLayout, _none);
        await _library.Create("price-drop", "второй", H0001Graph(), NoLayout, _none);

        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Create("funding-high", null, H0001Graph(), NoLayout, _none))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Rename("price-drop", "funding-high", _none))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That((await _library.List(_none)).Select(entry => entry.Name), Is.EquivalentTo(new[] { "funding-high", "price-drop" }));
    }

    [Test]
    public async Task Rename_gives_the_signal_its_new_name_and_keeps_the_rest()
    {
        await _library.Create("price-drop", "описание", H0001Graph(), NoLayout, _none);

        await _library.Rename("price-drop", "price-fall", _none);

        Assert.That((await _library.Read("price-fall", _none)).Description, Is.EqualTo("описание"));
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Read("price-drop", _none))!.Problem, Is.EqualTo(SignalProblem.NotFound));
    }

    [Test]
    public async Task Delete_removes_the_signal_from_the_list()
    {
        await _library.Create("price-drop", null, H0001Graph(), NoLayout, _none);

        await _library.Delete("price-drop", _none);

        Assert.That(await _library.List(_none), Is.Empty);
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _library.Delete("price-drop", _none))!.Problem, Is.EqualTo(SignalProblem.NotFound));
    }

    [Test]
    public async Task The_list_is_in_name_order()
    {
        await _library.Create("funding-high", null, H0001Graph(), NoLayout, _none);
        await _library.Create("candle-drop", "свеча", H0001Graph(), NoLayout, _none);

        IReadOnlyList<SignalEntry> list = await _library.List(_none);

        Assert.That(list.Select(entry => entry.Name), Is.EqualTo(new[] { "candle-drop", "funding-high" }));
        Assert.That(list[0], Is.EqualTo(new SignalEntry("candle-drop", "свеча", Version: 1, Error: null)));
    }

    private static JsonNode H0001GraphNode()
    {
        return JsonNode.Parse(HypothesisReaderTests.H0001Text)!["signal"]!.DeepClone();
    }

    private static JsonElement H0001Graph()
    {
        return Element(H0001GraphNode());
    }

    private static JsonElement Element(JsonNode node)
    {
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }
}
