using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     The list of kinds: the thirteen a signal is built from, with the arithmetic of change add-position-node (task 2.2) and
///     the numbers, windows, indicators and candles of change add-setup-strategies (task 2.1) and its <c>is-true</c> (task 8.1),
///     the position nodes of a strategy (change add-strategy-graph, task 1.1; change add-position-node, task 2.2) and the
///     <c>signal</c> node of a strategy (change add-strategy-constructor, task 2.1).
/// </summary>
public sealed class NodeCatalogTests
{
    private static readonly GraphDefinition Empty = new GraphDefinition([], [], [], []);

    private static readonly string[] SignalKinds =
        [
            "parameters", "candle", "funding-rate", "pair-candle", "compare", "previous", "and", "or", "not", "first-time", "add", "subtract", "multiply",
            "divide", "min", "max", "select", "window", "percentile", "hold", "atr", "rsi", "zones", "candles", "is-true", "outputs"
        ];

    private static readonly string[] PositionKinds =
        ["position", "enter", "market-order", "limit-order", "close-position", "stop-loss", "take-profit", "close-after"];

    [Test]
    public void All_holds_the_signal_kinds_then_the_position_kinds_then_the_signal_node_and_each_is_found_by_name()
    {
        Assert.That(NodeCatalog.All.Select(kind => kind.Name), Is.EqualTo(SignalKinds.Concat(PositionKinds).Append("signal")));
        Assert.That(NodeCatalog.All.Select(kind => NodeCatalog.Find(kind.Name)), Is.EqualTo(NodeCatalog.All));
    }

    [Test]
    public void The_kinds_of_H0001_and_the_arithmetic_are_allowed_in_a_signal()
    {
        Assert.That(NodeCatalog.All.Where(kind => kind.AllowedInSignal).Select(kind => kind.Name), Is.EqualTo(SignalKinds));
    }

    [Test]
    public void The_position_kinds_are_not_allowed_in_a_signal()
    {
        Assert.That(PositionKinds.Select(name => NodeCatalog.Find(name)!.AllowedInSignal), Has.All.False);
    }

    [Test]
    public void Enter_takes_an_event_and_a_size_gives_an_order_and_has_a_side()
    {
        NodeKind enter = NodeCatalog.Find(NodeCatalog.Enter)!;

        Assert.That(enter.Inputs(Empty), Is.EqualTo(new[] { new PinDeclaration("when", PinType.Event), new PinDeclaration("size", PinType.Number) }));
        Assert.That(enter.Outputs(Empty), Is.EqualTo(new[] { new PinDeclaration("order", PinType.Order) }));
        SettingDeclaration side = enter.Settings.Single();
        Assert.That(side.Name, Is.EqualTo("side"));
        Assert.That(side.Values, Is.EqualTo(new[] { "long", "short" }));
        Assert.That(side.Default, Is.Null, "a side is always written");
    }

    [TestCase("stop-loss")]
    [TestCase("take-profit")]
    public void Stop_loss_and_take_profit_take_the_position_and_a_distance(string name)
    {
        NodeKind kind = NodeCatalog.Find(name)!;

        Assert.That(kind.Inputs(Empty), Is.EqualTo(new[] { new PinDeclaration("position", PinType.Position), new PinDeclaration("distance", PinType.Number) }));
        Assert.That(kind.Outputs(Empty), Is.EqualTo(new[] { new PinDeclaration("order", PinType.Order) }));
        Assert.That(kind.Settings, Is.Empty);
    }

    [Test]
    public void Close_after_takes_the_position_and_a_duration()
    {
        NodeKind kind = NodeCatalog.Find(NodeCatalog.CloseAfter)!;

        Assert.That(kind.Inputs(Empty), Is.EqualTo(new[] { new PinDeclaration("position", PinType.Position), new PinDeclaration("after", PinType.Duration) }));
        Assert.That(kind.Outputs(Empty), Is.EqualTo(new[] { new PinDeclaration("order", PinType.Order) }));
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
    public void The_settings_of_every_kind_but_signal_are_not_free()
    {
        Assert.That(NodeCatalog.All.Where(kind => kind.Name is not ("signal" or "pair-candle")).SelectMany(kind => kind.Settings).Select(setting => setting.Free), Has.All.False);
    }

    /// <summary>
    ///     The pins of every kind but <c>signal</c>: a node and a set of signals change none of them. The position nodes have
    ///     the pins of change add-position-node (design.md, decision 3); the inputs of <c>position</c> and the input
    ///     <c>orders</c> take several wires.
    /// </summary>
    [Test]
    public void The_pins_of_every_kind_but_signal()
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
            "pair-candle: -> open:number high:number low:number close:number volume:number",
            "compare: a:number b:number -> result:boolean",
            "previous: value:number -> result:number",
            "and: a:boolean b:boolean -> result:boolean",
            "or: a:boolean b:boolean -> result:boolean",
            "not: value:boolean -> result:boolean",
            "first-time: value:boolean -> event:event",
            "add: a:number b:number -> result:number",
            "subtract: a:number b:number -> result:number",
            "multiply: a:number b:number -> result:number",
            "divide: a:number b:number -> result:number",
            "min: a:number b:number -> result:number",
            "max: a:number b:number -> result:number",
            "select: when:boolean a:number b:number -> result:number",
            "window: value:number length:number -> result:number",
            "percentile: value:number length:number rank:number -> result:number",
            "hold: when:boolean value:number length:number -> result:number",
            "atr: length:number -> result:number",
            "rsi: value:number length:number -> result:number",
            "zones: atr:number price:number length:number merge:number strength:number -> above:number below:number",
            "candles: count:number -> duration:duration",
            "is-true: value:boolean -> result:boolean",
            "outputs: entry:event ->",
            "position: orders:order* -> position:position open:boolean quantity:number averagePrice:number lastEntryPrice:number invested:number sinceLastEntry:duration",
            "enter: when:event size:number -> order:order",
            "market-order: when:boolean size:number -> order:order",
            "limit-order: when:boolean price:number size:number -> order:order",
            "close-position: when:boolean -> order:order",
            "stop-loss: position:position distance:number -> order:order",
            "take-profit: position:position distance:number -> order:order",
            "close-after: position:position after:duration -> order:order"
        ];

        foreach (NodeKind kind in NodeCatalog.All.Where(kind => kind.Name != "signal"))
        {
            Assert.That(kind.Inputs(node, graph, Graph.SignalLibrary.Set), Is.EqualTo(kind.Inputs(graph)), kind.Name);
            Assert.That(kind.Outputs(node, graph, Graph.SignalLibrary.Set), Is.EqualTo(kind.Outputs(graph)), kind.Name);
        }

        Assert.That(NodeCatalog.All.Where(kind => kind.Name != "signal").Select(kind => Pins(kind, graph)), Is.EqualTo(expected));
    }

    [TestCase("market-order")]
    [TestCase("limit-order")]
    public void Market_and_limit_orders_have_a_side_and_a_unit_of_size_that_are_always_written(string name)
    {
        NodeKind kind = NodeCatalog.Find(name)!;

        Assert.That(
            kind.Settings.Select(setting => (setting.Name, string.Join(",", setting.Values), setting.Default)),
            Is.EqualTo(new[] { ("side", "buy,sell", (string?)null), ("sizeIn", "amount,quantity", (string?)null) }));
    }

    [Test]
    public void The_arithmetic_is_allowed_in_a_signal_and_the_position_kinds_are_not()
    {
        Assert.That(new[] { "add", "subtract", "multiply" }.Select(name => NodeCatalog.Find(name)!.AllowedInSignal), Has.All.True);
        Assert.That(PositionKinds.Select(name => NodeCatalog.Find(name)!.AllowedInSignal), Has.All.False);
    }

    private static string Pins(NodeKind kind, GraphDefinition graph)
    {
        string inputs = string.Join(" ", kind.Inputs(graph).Select(pin => $"{pin.Name}:{PinTypes.Name(pin.Type)}{(pin.Many ? "*" : "")}"));
        string outputs = string.Join(" ", kind.Outputs(graph).Select(pin => $"{pin.Name}:{PinTypes.Name(pin.Type)}"));
        return $"{kind.Name}: {inputs}{(inputs.Length > 0 ? " " : "")}->{(outputs.Length > 0 ? " " : "")}{outputs}";
    }
}
