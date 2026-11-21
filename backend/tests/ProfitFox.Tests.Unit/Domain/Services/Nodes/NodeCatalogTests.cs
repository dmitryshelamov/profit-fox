using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     The list of kinds: the ten a signal is built from, and the four position nodes of a strategy (change
///     add-strategy-graph, task 1.1).
/// </summary>
public sealed class NodeCatalogTests
{
    private static readonly GraphDefinition Empty = new GraphDefinition([], [], [], []);

    private static readonly string[] SignalKinds =
        ["parameters", "candle", "funding-rate", "compare", "previous", "and", "or", "not", "first-time", "outputs"];

    private static readonly string[] PositionKinds = ["enter", "stop-loss", "take-profit", "close-after"];

    [Test]
    public void All_holds_the_signal_kinds_then_the_position_kinds_and_each_is_found_by_name()
    {
        Assert.That(NodeCatalog.All.Select(kind => kind.Name), Is.EqualTo(SignalKinds.Concat(PositionKinds)));
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
}
