using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Spec strategy-graph «Вид стратегии» (change add-strategy-backtest, task 2.1): a category of ideas is a strategy by
///     plan, each broken rule is an error of its own, and a strategy for a bot needs only the checks of a strategy.
/// </summary>
public sealed class StrategyKindTests
{
    [Test]
    public void H0001_is_a_category_of_ideas()
    {
        Assert.That(Errors(Graphs.H0001Strategy, StrategyKind.Idea), Is.Empty);
    }

    [Test]
    public void The_ladder_is_not_a_category_of_ideas()
    {
        List<GraphError> errors = Errors(Graphs.Ladder, StrategyKind.Idea);

        Assert.That(
            errors.Where(error => error.Code == GraphErrorCode.StrategyKindNodeNotAllowed).Select(error => error.Node),
            Is.EqualTo(new[] { "entry", "sell", "buy" }),
            "the market order and both limit orders, in the order of the graph");
        Assert.That(errors.Count(error => error.Code == GraphErrorCode.StrategyKindNeedsTime), Is.EqualTo(1));
        Assert.That(errors.Single(error => error.Code == GraphErrorCode.StrategyKindNeedsTime).Reason, Does.Contain("close-after"));
        Assert.That(errors.Where(error => error.Code == GraphErrorCode.StrategyKindReadsPosition), Is.Not.Empty, "the ladder reads the state of the position");
    }

    [Test]
    public void A_category_of_ideas_without_a_time_needs_close_after()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveNodeAndWires("close-after");
            graph.AddExit("stop", "stop-loss", "distance", JsonNode.Parse("0.02")!);
            graph.AddExit("target", "take-profit", "distance", JsonNode.Parse("0.04")!);
        });

        GraphError error = Errors(json, StrategyKind.Idea).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyKindNeedsTime));
        Assert.That(error.Reason, Does.Contain("close-after"));
    }

    [Test]
    public void The_ladder_is_a_strategy_for_a_bot()
    {
        Assert.That(Errors(Graphs.Ladder, StrategyKind.Bot), Is.Empty);
    }

    [Test]
    public void A_wire_from_the_state_of_the_position_is_reported_on_the_wire()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddNode("long", "compare", "b", JsonNode.Parse("100")!);
            graph.Node("long")["settings"] = new JsonObject { ["operation"] = "gt" };
            graph.AddWire("position.quantity", "long.a");
        });

        GraphError error = Errors(json, StrategyKind.Idea).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyKindReadsPosition));
        Assert.That(error.Wire, Is.EqualTo(new WireDefinition(new PinReference("position", "quantity"), new PinReference("long", "a"))));
    }

    [TestCase("market-order")]
    [TestCase("limit-order")]
    [TestCase("close-position")]
    public void A_node_of_an_order_is_reported_on_its_node(string kind)
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            var node = new JsonObject { ["id"] = "x", ["kind"] = kind, ["inputs"] = new JsonObject { ["when"] = false } };
            if (kind != "close-position")
            {
                node["settings"] = new JsonObject { ["side"] = "buy", ["sizeIn"] = "amount" };
                node["inputs"]!["size"] = 100;
            }

            if (kind == "limit-order")
            {
                node["inputs"]!["price"] = 90;
            }

            graph.Nodes().Add(node);
            graph.AddWire("x.order", "position.orders");
        });

        GraphError error = Errors(json, StrategyKind.Idea).Single();

        Assert.That((error.Code, error.Node), Is.EqualTo((GraphErrorCode.StrategyKindNodeNotAllowed, (string?)"x")));
        Assert.That(Errors(json, StrategyKind.Bot), Is.Empty);
    }

    [Test]
    public void A_strategy_is_by_plan_exactly_when_it_passes_as_a_category_of_ideas()
    {
        foreach (string json in new[] { Graphs.H0001Strategy, Graphs.Ladder })
        {
            TradingStrategy strategy = StrategyGraph.ReadStrategy(json).Value!;

            Assert.That(strategy.ByPlan, Is.EqualTo(Errors(json, StrategyKind.Idea).Count == 0));
            Assert.That(StrategyKindRules.IsByPlan(strategy.Graph), Is.EqualTo(strategy.ByPlan));
        }
    }

    [Test]
    public void The_kinds_are_written_as_bot_and_idea()
    {
        Assert.That(StrategyKinds.Name(StrategyKind.Bot), Is.EqualTo("bot"));
        Assert.That(StrategyKinds.Name(StrategyKind.Idea), Is.EqualTo("idea"));
        Assert.That(StrategyKinds.TryParse("idea", out StrategyKind idea) && idea == StrategyKind.Idea);
        Assert.That(StrategyKinds.TryParse("bot", out StrategyKind bot) && bot == StrategyKind.Bot);
        Assert.That(StrategyKinds.TryParse("plan", out _), Is.False);
        Assert.That(StrategyKinds.TryParse(null, out _), Is.False);
    }

    private static List<GraphError> Errors(string json, StrategyKind kind)
    {
        return [.. StrategyGraph.ReadStrategy(json, kind).Errors];
    }
}
