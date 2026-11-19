using System.Text.Json;
using System.Text.Json.Nodes;
using ProfitFox.Bench.Application.Signals;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Bench.Infrastructure.Signals;
using ProfitFox.Engine.Strategy.Graph.Reading;
using ProfitFox.Tests.Unit.Bench.Hypotheses;

namespace ProfitFox.Tests.Unit.Bench.Signals;

/// <summary>
///     Spec signal-constructor: the signal file, its name, saving only a graph without errors, and the operations of the
///     library (task 2.1).
/// </summary>
public sealed class SignalLibraryTests
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];

    private string _folder = "";
    private SignalLibrary _library = null!;

    [SetUp]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "profit-fox-signals-" + Guid.NewGuid().ToString("N"));
        _library = new SignalLibrary(new SignalFiles(_folder));
    }

    [TearDown]
    public void Remove()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Test]
    public void A_saved_signal_holds_its_name_and_reads_as_the_signal_of_H0001()
    {
        _library.Create("funding-high", "Фандинг впервые не ниже порога", H0001Graph(), NoLayout);

        using JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "funding-high.json")));
        Assert.That(file.RootElement.GetProperty("name").GetString(), Is.EqualTo("funding-high"));
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(file.RootElement.GetProperty("graph").GetRawText()), H0001GraphNode()), Is.True);

        SignalDocument read = _library.Read("funding-high");
        Assert.That(read.Description, Is.EqualTo("Фандинг впервые не ниже порога"));
        Assert.That(read.Graph.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "p", "rate", "high", "first", "out" }));
    }

    [TestCase("Funding High")]
    [TestCase("Funding")]
    [TestCase("funding--high")]
    [TestCase("-funding")]
    [TestCase("../funding")]
    [TestCase("")]
    public void A_name_against_the_rule_is_refused_and_nothing_is_written(string name)
    {
        SignalLibraryException error = Assert.Throws<SignalLibraryException>(() => _library.Create(name, null, H0001Graph(), NoLayout))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => item.Field), Has.Member("name"));
        Assert.That(Directory.Exists(_folder) ? Directory.GetFiles(_folder) : [], Is.Empty);
    }

    [Test]
    public void A_name_of_65_characters_is_refused()
    {
        Assert.That(SignalNames.IsValid(new string('a', 64)), Is.True);
        Assert.That(SignalNames.IsValid(new string('a', 65)), Is.False);
    }

    [Test]
    public void A_graph_with_an_error_is_refused_and_the_saved_file_stays()
    {
        _library.Create("funding-high", null, H0001Graph(), NoLayout);
        string before = File.ReadAllText(Path.Combine(_folder, "funding-high.json"));
        JsonNode broken = H0001GraphNode();
        broken["wires"]!.AsArray().RemoveAt(1);

        SignalLibraryException error = Assert.Throws<SignalLibraryException>(() => _library.Replace("funding-high", null, Element(broken), NoLayout))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => (item.Code, item.Node, item.Pin)), Is.EqualTo(new[] { (GraphErrorCode.MissingInput, (string?)"high", (string?)"b") }));
        Assert.That(File.ReadAllText(Path.Combine(_folder, "funding-high.json")), Is.EqualTo(before));
    }

    [Test]
    public void Moving_a_node_changes_only_the_layout()
    {
        _library.Create("funding-high", null, H0001Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(0, 120) });
        string graphBefore = GraphText("funding-high");

        _library.Replace("funding-high", null, H0001Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(40, 200.5) });

        Assert.That(GraphText("funding-high"), Is.EqualTo(graphBefore));
        Assert.That(_library.Read("funding-high").Layout["rate"], Is.EqualTo(new NodePosition(40, 200.5)));
    }

    [Test]
    public void The_layout_keeps_only_nodes_of_the_graph()
    {
        _library.Create("funding-high", null, H0001Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(0, 0), ["gone"] = new(1, 1) });

        Assert.That(_library.Read("funding-high").Layout.Keys, Is.EqualTo(new[] { "rate" }));
    }

    [Test]
    public void A_taken_name_is_refused_on_create_and_rename_and_both_files_stay()
    {
        _library.Create("funding-high", null, H0001Graph(), NoLayout);
        _library.Create("price-drop", "второй", H0001Graph(), NoLayout);
        string funding = File.ReadAllText(Path.Combine(_folder, "funding-high.json"));
        string price = File.ReadAllText(Path.Combine(_folder, "price-drop.json"));

        Assert.That(Assert.Throws<SignalLibraryException>(() => _library.Create("funding-high", null, H0001Graph(), NoLayout))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That(Assert.Throws<SignalLibraryException>(() => _library.Rename("price-drop", "funding-high"))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That(File.ReadAllText(Path.Combine(_folder, "funding-high.json")), Is.EqualTo(funding));
        Assert.That(File.ReadAllText(Path.Combine(_folder, "price-drop.json")), Is.EqualTo(price));
    }

    [Test]
    public void Rename_moves_the_file_and_its_name()
    {
        _library.Create("price-drop", "описание", H0001Graph(), NoLayout);

        _library.Rename("price-drop", "price-fall");

        Assert.That(File.Exists(Path.Combine(_folder, "price-drop.json")), Is.False);
        Assert.That(_library.Read("price-fall").Name, Is.EqualTo("price-fall"));
        Assert.That(_library.Read("price-fall").Description, Is.EqualTo("описание"));
    }

    [Test]
    public void Delete_removes_the_file_from_the_list()
    {
        _library.Create("price-drop", null, H0001Graph(), NoLayout);

        _library.Delete("price-drop");

        Assert.That(File.Exists(Path.Combine(_folder, "price-drop.json")), Is.False);
        Assert.That(_library.List(), Is.Empty);
        Assert.That(Assert.Throws<SignalLibraryException>(() => _library.Delete("price-drop"))!.Problem, Is.EqualTo(SignalProblem.NotFound));
    }

    [Test]
    public void The_list_is_in_name_order_and_keeps_a_broken_file_with_its_error()
    {
        _library.Create("funding-high", null, H0001Graph(), NoLayout);
        _library.Create("candle-drop", "свеча", H0001Graph(), NoLayout);
        File.WriteAllText(Path.Combine(_folder, "broken.json"), """{ "name": "broken", "layout": {} }""");

        IReadOnlyList<SignalEntry> list = _library.List();

        Assert.That(list.Select(entry => entry.Name), Is.EqualTo(new[] { "broken", "candle-drop", "funding-high" }));
        Assert.That(list[0].Error, Does.Contain("graph"));
        Assert.That(list[1], Is.EqualTo(new SignalEntry("candle-drop", "свеча", Error: null)));
        Assert.That(Assert.Throws<SignalLibraryException>(() => _library.Read("broken"))!.Problem, Is.EqualTo(SignalProblem.Unreadable));
    }

    [Test]
    public void A_file_whose_name_differs_from_its_file_does_not_read()
    {
        Directory.CreateDirectory(_folder);
        string text = $$"""{ "name": "other", "graph": {{H0001GraphNode().ToJsonString()}}, "layout": {} }""";
        File.WriteAllText(Path.Combine(_folder, "funding-high.json"), text);

        Assert.That(_library.List().Single().Error, Does.Contain("other"));
    }

    [Test]
    public void No_temporary_file_stays_after_writing()
    {
        _library.Create("funding-high", null, H0001Graph(), NoLayout);
        _library.Replace("funding-high", "снова", H0001Graph(), NoLayout);
        _library.Rename("funding-high", "funding");

        Assert.That(Directory.GetFiles(_folder).Select(Path.GetFileName), Is.EqualTo(new[] { "funding.json" }));
    }

    private string GraphText(string name)
    {
        using JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, name + ".json")));
        return file.RootElement.GetProperty("graph").GetRawText();
    }

    private static string HypothesisFile()
    {
        return Path.Combine(HypothesisReaderTests.H0001Folder, "hypothesis.json");
    }

    private static JsonNode H0001GraphNode()
    {
        return JsonNode.Parse(File.ReadAllText(HypothesisFile()))!["signal"]!.DeepClone();
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
