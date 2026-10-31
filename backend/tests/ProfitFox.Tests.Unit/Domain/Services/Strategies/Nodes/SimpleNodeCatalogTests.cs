using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Nodes;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Nodes;

public class SimpleNodeCatalogTests
{
    private static readonly PinContext NoContext = new PinContext([], [], []);

    private static IEnumerable<TestCaseData> Kinds()
    {
        yield return Case(SimpleNodeKind.Candle, "", "open:N high:N low:N close:N volume:N");
        yield return Case(SimpleNodeKind.Position, "", "hasPosition:B quantity:N averagePrice:N invested:N lastEntryPrice:N sinceLastEntry:D");
        yield return Case(SimpleNodeKind.Ema, "source:N period:N", "value:N");
        yield return Case(SimpleNodeKind.Cross, "a:N b:N", "up:B down:B");
        yield return Case(SimpleNodeKind.Add, "a:N b:N", "result:N");
        yield return Case(SimpleNodeKind.Subtract, "a:N b:N", "result:N");
        yield return Case(SimpleNodeKind.Multiply, "a:N b:N", "result:N");
        yield return Case(SimpleNodeKind.Divide, "a:N b:N", "result:N");
        yield return Case(SimpleNodeKind.Compare, "a:N b:N", "result:B");
        yield return Case(SimpleNodeKind.And, "a:B b:B", "result:B");
        yield return Case(SimpleNodeKind.Or, "a:B b:B", "result:B");
        yield return Case(SimpleNodeKind.Not, "value:B", "result:B");
        yield return Case(SimpleNodeKind.MarketOrder, "when:B size:N", "");
        yield return Case(SimpleNodeKind.ClosePosition, "when:B", "");
        yield return Case(SimpleNodeKind.HoldLimit, "when:B price:N size:N", "");
    }

    [TestCaseSource(nameof(Kinds))]
    public void Simple_node_has_its_pins(SimpleNodeKind kind, string inputs, string outputs)
    {
        NodePins pins = SimpleNodeCatalog.Pins(kind, new NodeSettings(), NoContext);

        Describe(pins.Inputs).ShouldBe(inputs);
        Describe(pins.Outputs).ShouldBe(outputs);
    }

    [Test]
    public void Compare_of_durations_takes_durations()
    {
        NodePins pins = SimpleNodeCatalog.Pins(SimpleNodeKind.Compare, new NodeSettings(ValueType: PinType.Duration), NoContext);

        Describe(pins.Inputs).ShouldBe("a:D b:D");
    }

    [Test]
    public void Context_nodes_take_their_pins_from_the_context()
    {
        var context = new PinContext(
            [new StrategyParameter("amount", PinType.Number, new NumberValue(1m))],
            [new PinDeclaration("fast", PinType.Number)],
            [new PinDeclaration("up", PinType.Boolean)]);

        Describe(SimpleNodeCatalog.Pins(SimpleNodeKind.Parameters, new NodeSettings(), context).Outputs).ShouldBe("amount:N");
        Describe(SimpleNodeCatalog.Pins(SimpleNodeKind.Inputs, new NodeSettings(), context).Outputs).ShouldBe("fast:N");
        Describe(SimpleNodeCatalog.Pins(SimpleNodeKind.Outputs, new NodeSettings(), context).Inputs).ShouldBe("up:B");
    }

    [Test]
    public void Every_kind_is_described()
    {
        Enum.GetValues<SimpleNodeKind>().ShouldAllBe(kind => SimpleNodeCatalog.Pins(kind, new NodeSettings(), NoContext) != null);
    }

    private static TestCaseData Case(SimpleNodeKind kind, string inputs, string outputs)
    {
        return new TestCaseData(kind, inputs, outputs).SetArgDisplayNames(kind.ToString());
    }

    private static string Describe(IReadOnlyList<PinDeclaration> pins)
    {
        return string.Join(" ", pins.Select(pin => $"{pin.Name}:{pin.Type.ToString()[0]}"));
    }
}