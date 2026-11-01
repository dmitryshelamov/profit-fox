using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Strategies;

public class StrategyJsonTests
{
    private const string Ema = """
        {
          "formatVersion": 1,
          "direction": "long",
          "parameters": [
            { "name": "fast", "type": "number", "default": 12, "min": 2, "max": 200, "integer": true },
            { "name": "amount", "type": "number", "default": 0.1 },
            { "name": "every", "type": "duration", "default": "01:00:00" }
          ],
          "nodes": [
            { "id": "params", "kind": "parameters" },
            { "id": "candle", "kind": "candle" },
            { "id": "cross", "kind": "composite", "ref": "ema-cross", "inputs": { "slow": 26 } },
            { "id": "mine", "kind": "composite", "ref": "0b8f2c1e-5d4a-4e3b-9c2d-1a2b3c4d5e6f" },
            { "id": "due", "kind": "compare", "settings": { "operation": "ge", "type": "duration" } },
            { "id": "flag", "kind": "and", "inputs": { "a": true } },
            { "id": "buy", "kind": "market-order", "settings": { "side": "buy", "size": "amount" } },
            { "id": "hold", "kind": "hold-limit", "settings": { "side": "sell", "size": "quantity" }, "inputs": { "price": 101.50 } }
          ],
          "wires": [
            { "from": "candle.close", "to": "cross.source" },
            { "from": "params.fast", "to": "cross.fast" }
          ],
          "layout": { "params": { "x": 0, "y": -12.5 } }
        }
        """;

    [Test]
    public void Strategy_is_read_and_written_back_unchanged()
    {
        StrategyDefinition definition = StrategyJson.ParseStrategy(Parse(Ema));

        JsonNode.DeepEquals(StrategyJson.WriteStrategy(definition), JsonNode.Parse(Ema)).ShouldBeTrue();
    }

    [Test]
    public void Snapshot_is_written_as_the_strategy_and_its_nodes_and_read_back_equal()
    {
        var custom = new CustomNodeId(Guid.Parse("0b8f2c1e-5d4a-4e3b-9c2d-1a2b3c4d5e6f"));
        var node = new CompositeDefinition([new PinDeclaration("x", PinType.Number)], [], new Graph([], [], new Dictionary<string, NodePosition>()));
        StrategyDefinition strategy = StrategyJson.ParseStrategy(Parse(Ema));
        var snapshot = StrategySnapshot.Of(strategy, new CompositeCatalog(new Dictionary<CompositeNodeId, CompositeDefinition>
        {
            [custom] = node,
            [new BuiltInNodeId(BuiltInNode.EmaCross)] = BuiltInComposites.Catalog.Find(new BuiltInNodeId(BuiltInNode.EmaCross))!
        }));

        JsonObject json = StrategyJson.WriteSnapshot(snapshot);

        json["strategy"]!["nodes"]!.AsArray().Count.ShouldBe(8);
        json["composites"]!.AsArray().Select(item => item!["ref"]!.GetValue<string>()).ShouldBe(["ema-cross", custom.Value.ToString()]);
        JsonNode.DeepEquals(json["composites"]![1]!["definition"], StrategyJson.WriteComposite(node)).ShouldBeTrue();
        StrategyJson.ParseSnapshot(Parse(json.ToJsonString())).ShouldBe(snapshot);
    }

    [Test]
    public void Strategy_is_read_into_the_domain()
    {
        StrategyDefinition definition = StrategyJson.ParseStrategy(Parse(Ema));

        definition.Direction.ShouldBe(StrategyDirection.Long);
        definition.Parameters[0].ShouldBe(new StrategyParameter("fast", PinType.Number, new NumberValue(12m), 2m, 200m, Integer: true));
        definition.Parameters[1].Default.ShouldBe(new NumberValue(0.1m));
        definition.Parameters[2].Default.ShouldBe(new DurationValue(TimeSpan.FromHours(1)));
        definition.Graph.Nodes[2].Type.ShouldBe(new CompositeNodeType(new BuiltInNodeId(BuiltInNode.EmaCross)));
        definition.Graph.Nodes[3].Type.ShouldBe(new CompositeNodeType(new CustomNodeId(Guid.Parse("0b8f2c1e-5d4a-4e3b-9c2d-1a2b3c4d5e6f"))));
        definition.Graph.Nodes[4].Settings.ShouldBe(new NodeSettings(PinType.Duration, CompareOperation.GreaterOrEqual));
        definition.Graph.Nodes[6].Settings.ShouldBe(new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount));
        definition.Graph.Nodes[7].Inputs["price"].ShouldBe(new NumberValue(101.50m));
        definition.Graph.Wires[0].ShouldBe(new Wire(new PinRef("candle", "close"), new PinRef("cross", "source")));
        definition.Graph.Layout["params"].ShouldBe(new NodePosition(0m, -12.5m));
    }

    [Test]
    public void Composite_is_read_and_written_back_unchanged()
    {
        const string composite = """
            {
              "formatVersion": 1,
              "inputs": [ { "name": "x", "type": "number" } ],
              "outputs": [ { "name": "on", "type": "boolean" } ],
              "nodes": [ { "id": "in", "kind": "inputs" }, { "id": "out", "kind": "outputs" } ],
              "wires": [],
              "layout": {}
            }
            """;

        CompositeDefinition definition = StrategyJson.ParseComposite(Parse(composite));

        definition.Inputs.ShouldBe([new PinDeclaration("x", PinType.Number)]);
        JsonNode.DeepEquals(StrategyJson.WriteComposite(definition), JsonNode.Parse(composite)).ShouldBeTrue();
    }

    [TestCase("""{ "formatVersion": 2, "direction": "long", "nodes": [] }""", "definition.formatVersion")]
    [TestCase("""{ "direction": "long", "nodes": [] }""", "definition.formatVersion")]
    [TestCase("""{ "formatVersion": 1, "direction": "up", "nodes": [] }""", "definition.direction")]
    [TestCase("""{ "formatVersion": 1, "direction": "long" }""", "definition.nodes")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "rsi" } ] }""", "definition.nodes[0].kind")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a/b", "kind": "candle" } ] }""", "definition.nodes[0].id")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "kind": "candle" } ] }""", "definition.nodes[0].id")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "composite", "ref": "nope" } ] }""", "definition.nodes[0].ref")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "market-order", "settings": { "size": "amount" } } ] }""", "definition.nodes[0].settings.side")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "hold-limit", "settings": { "side": "buy" } } ] }""", "definition.nodes[0].settings.size")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "compare" } ] }""", "definition.nodes[0].settings.operation")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "market-order" } ] }""", "definition.nodes[0].settings.side")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "compare", "settings": { "operation": "ge", "type": "text" } } ] }""", "definition.nodes[0].settings.type")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "add", "inputs": { "a": "soon" } } ] }""", "definition.nodes[0].inputs.a")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "wires": [ { "from": "candle", "to": "a.b" } ] }""", "definition.wires[0].from")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": "", "type": "number", "default": 1 } ] }""", "definition.parameters[0].name")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": "p", "type": "boolean", "default": true } ] }""", "definition.parameters[0].type")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": "p", "type": "number", "default": "x" } ] }""", "definition.parameters[0].default")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "layout": { "a": { "x": 1 } } }""", "definition.layout.a")]
    [TestCase("""{ "formatVersion": 1e30, "direction": "long", "nodes": [] }""", "definition.formatVersion")]
    [TestCase("""{ "formatVersion": 1, "direction": 5, "nodes": [] }""", "definition.direction")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": 5 } ] }""", "definition.nodes[0].kind")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "composite", "ref": 5 } ] }""", "definition.nodes[0].ref")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "wires": [ { "from": 5, "to": "a.b" } ] }""", "definition.wires[0].from")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": 5, "type": "number", "default": 1 } ] }""", "definition.parameters[0].name")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": "p", "type": "number" } ] }""", "definition.parameters[0].default")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "compare", "settings": { "operation": "ge", "type": "boolean" } } ] }""", "definition.nodes[0].settings.type")]
    public void Malformed_strategy_is_rejected_with_the_path_of_the_problem(string json, string key)
    {
        ValidationFailedException exception = Should.Throw<ValidationFailedException>(() => StrategyJson.ParseStrategy(Parse(json)));

        exception.Errors.Keys.ShouldContain(key);
    }

    [TestCase("""{ "formatVersion": 1, "outputs": [], "nodes": [] }""", "definition.inputs")]
    [TestCase("""{ "formatVersion": 1, "inputs": [ { "name": "x", "type": "colour" } ], "outputs": [], "nodes": [] }""", "definition.inputs[0].type")]
    public void Malformed_composite_is_rejected_with_the_path_of_the_problem(string json, string key)
    {
        ValidationFailedException exception = Should.Throw<ValidationFailedException>(() => StrategyJson.ParseComposite(Parse(json)));

        exception.Errors.Keys.ShouldContain(key);
    }

    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": "p", "type": "number", "default": 1, "min": "two" } ] }""")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "layout": { "a": { "x": "left", "y": 1 } } }""")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "a", "kind": "candle", "inputs": [] } ] }""")]
    [TestCase("""{ "formatVersion": 1, "direction": "long", "nodes": [], "parameters": [ { "name": "p", "type": "number", "default": 1, "max": 1e30 } ] }""")]
    public void Value_of_a_wrong_json_type_is_rejected(string json)
    {
        Should.Throw<ValidationFailedException>(() => StrategyJson.ParseStrategy(Parse(json))).Errors.Keys.ShouldContain("definition");
    }

    [Test]
    public void Definition_that_is_not_an_object_is_rejected()
    {
        Should.Throw<ValidationFailedException>(() => StrategyJson.ParseStrategy(Parse("[]"))).Errors.Keys.ShouldContain("definition");
    }

    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }
}