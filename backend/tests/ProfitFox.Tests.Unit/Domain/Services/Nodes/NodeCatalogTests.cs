using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     The list of kinds: the ten a signal is built from, the four position nodes of a strategy (change add-strategy-graph,
///     task 1.1) and the <c>signal</c> node of a strategy (change add-strategy-constructor, task 2.1).
/// </summary>
public sealed class NodeCatalogTests
{
    private static readonly GraphDefinition Empty = new GraphDefinition([], [], [], []);

    private static readonly string[] SignalKinds =
        ["parameters", "candle", "funding-rate", "compare", "previous", "and", "or", "not", "first-time", "outputs"];

    private static readonly string[] PositionKinds = ["enter", "stop-loss", "take-profit", "close-after"];

    [Test]
    public void All_holds_the_signal_kinds_then_the_position_kinds_then_the_signal_node_and_each_is_found_by_name()
    {
        Assert.That(NodeCatalog.All.Select(kind => kind.Name), Is.EqualTo(SignalKinds.Concat(PositionKinds).Append("signal")));
        Assert.That(NodeCatalog.All.Select(kind => NodeCatalog.Find(kind.Name)), Is.EqualTo(NodeCatalog.All));
    }

    [Test]
    public void The_ten_kinds_of_H0001_are_allowed_in_a_signal()
    {
        Assert.That(NodeCatalog.All.Where(kind => kind.AllowedInSignal).Select(kind => kind.Name), Is.EqualTo(SignalKinds));
    }

    [Test]
    public void The_position_kinds_are_not_allowed_in_a_signal()
    {
        Assert.That(PositionKinds.Select(name => NodeCatalog.Find(name)!.AllowedInSignal), Has.All.False);
    }

    [Test]
    public void Enter_takes_an_event_and_has_a_side()
    {
        NodeKind enter = NodeCatalog.Find(NodeCatalog.Enter)!;

        Assert.That(enter.Inputs(Empty), Is.EqualTo(new[] { new PinDeclaration("when", PinType.Event) }));
        Assert.That(enter.Outputs(Empty), Is.Empty);
        SettingDeclaration side = enter.Settings.Single();
        Assert.That(side.Name, Is.EqualTo("side"));
        Assert.That(side.Values, Is.EqualTo(new[] { "long", "short" }));
        Assert.That(side.Default, Is.Null, "a side is always written");
    }

    [TestCase("stop-loss")]
    [TestCase("take-profit")]
    public void Stop_loss_and_take_profit_take_a_distance(string name)
    {
        NodeKind kind = NodeCatalog.Find(name)!;

        Assert.That(kind.Inputs(Empty), Is.EqualTo(new[] { new PinDeclaration("distance", PinType.Number) }));
        Assert.That(kind.Outputs(Empty), Is.Empty);
        Assert.That(kind.Settings, Is.Empty);
    }

    [Test]
    public void Close_after_takes_a_duration()
    {
        NodeKind kind = NodeCatalog.Find(NodeCatalog.CloseAfter)!;

        Assert.That(kind.Inputs(Empty), Is.EqualTo(new[] { new PinDeclaration("after", PinType.Duration) }));
        Assert.That(kind.Outputs(Empty), Is.Empty);
        Assert.That(kind.Settings, Is.Empty);
    }

    [Test]
    public void The_signal_node_points_at_a_version_by_two_free_settings_and_gives_an_event()
    {
        NodeKind signal = NodeCatalog.Find(NodeCatalog.Signal)!;

        Assert.That(signal.AllowedInSignal, Is.False);
        Assert.That(signal.Inputs(Empty), Is.Empty, "the inputs come from the version the node points at");
        Assert.That(signal.Outputs(Empty), Is.EqualTo(new[] { new PinDeclaration("event", PinType.Event) }));
        Assert.That(signal.Settings.Select(setting => (setting.Name, setting.Free, setting.Values.Count, setting.Default)),
            Is.EqualTo(new[] { ("signal", true, 0, (string?)null), ("version", true, 0, (string?)null) }));
    }

    [Test]
    public void The_settings_of_the_fourteen_kinds_are_not_free()
    {
        Assert.That(NodeCatalog.All.Where(kind => kind.Name != "signal").SelectMany(kind => kind.Settings).Select(setting => setting.Free), Has.All.False);
    }

    /// <summary>
    ///     The pins of the fourteen kinds as they were before the <c>signal</c> node: a node and a set of signals change
    ///     none of them.
    /// </summary>
    [Test]
    public void The_pins_of_the_fourteen_kinds_did_not_change()
    {
        var graph = new GraphDefinition(
            [new ParameterDefinition("threshold", PinType.Number, GraphValue.FromNumber(0.0005m), null, null)],
            [new OutputDefinition("entry", PinType.Event)],
            [],
            []);
        var node = new NodeDefinition("n", "any", new Dictionary<string, string>(), new Dictionary<string, GraphValue>());
        string[] expected =
        [
            "parameters: -> threshold:number",
            "candle: -> open:number high:number low:number close:number volume:number",
            "funding-rate: -> value:number",
            "compare: a:number b:number -> result:boolean",
            "previous: value:number -> result:number",
            "and: a:boolean b:boolean -> result:boolean",
            "or: a:boolean b:boolean -> result:boolean",
            "not: value:boolean -> result:boolean",
            "first-time: value:boolean -> event:event",
            "outputs: entry:event ->",
            "enter: when:event ->",
            "stop-loss: distance:number ->",
            "take-profit: distance:number ->",
            "close-after: after:duration ->"
        ];

        foreach (NodeKind kind in NodeCatalog.All.Where(kind => kind.Name != "signal"))
        {
            Assert.That(kind.Inputs(node, graph, Graph.SignalLibrary.Set), Is.EqualTo(kind.Inputs(graph)), kind.Name);
            Assert.That(kind.Outputs(node, graph, Graph.SignalLibrary.Set), Is.EqualTo(kind.Outputs(graph)), kind.Name);
        }

        Assert.That(NodeCatalog.All.Where(kind => kind.Name != "signal").Select(kind => Pins(kind, graph)), Is.EqualTo(expected));
    }

    private static string Pins(NodeKind kind, GraphDefinition graph)
    {
        string inputs = string.Join(" ", kind.Inputs(graph).Select(pin => $"{pin.Name}:{PinTypes.Name(pin.Type)}"));
        string outputs = string.Join(" ", kind.Outputs(graph).Select(pin => $"{pin.Name}:{PinTypes.Name(pin.Type)}"));
        return $"{kind.Name}: {inputs}{(inputs.Length > 0 ? " " : "")}->{(outputs.Length > 0 ? " " : "")}{outputs}";
    }
}
