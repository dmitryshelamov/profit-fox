using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Execution;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies;

/// <summary>
///     Change add-position-node, tasks 5.1 and 5.2: a strategy by plan (spec strategy-position «Стратегия по плану»), the
///     size of the entry its run reads, and a cycle outside the graph only for it («Открытие вне графа»).
/// </summary>
public sealed class ByPlanTests
{
    [Test]
    public void H0001_is_by_plan()
    {
        Assert.That(Read(Graphs.H0001Strategy).ByPlan, Is.True);
    }

    [Test]
    public void A_stop_and_a_target_without_close_after_are_not_by_plan()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveNodeAndWires("close-after");
            graph.AddExit("stop", "stop-loss", "distance", JsonNode.Parse("0.02")!);
            graph.AddExit("target", "take-profit", "distance", JsonNode.Parse("0.04")!);
        });

        Assert.That(Read(json).ByPlan, Is.False, "a trade of a plan has a time");
    }

    [Test]
    public void The_ladder_is_a_strategy_with_orders()
    {
        Assert.That(Read(Graphs.Ladder).ByPlan, Is.False);
    }

    [Test]
    public void A_wire_from_the_state_of_the_position_makes_a_strategy_with_orders()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddNode("long", "compare", "b", JsonNode.Parse("100")!);
            graph.Node("long")["settings"] = new JsonObject { ["operation"] = "gt" };
            graph.AddWire("position.quantity", "long.a");
        });

        Assert.That(Read(json).ByPlan, Is.False);
    }

    [TestCase("market-order")]
    [TestCase("limit-order")]
    [TestCase("close-position")]
    public void A_node_of_an_order_with_a_price_or_a_condition_makes_a_strategy_with_orders(string kind)
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

        Assert.That(Read(json).ByPlan, Is.False);
    }

    [Test]
    public void The_run_of_a_strategy_by_plan_reads_the_size_of_the_entry()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "limitPerOrder", ["type"] = "number", ["value"] = 50 });
            graph.Node("enter")["inputs"] = new JsonObject();
            graph.AddWire("p.limitPerOrder", "enter.size");
        });
        var executor = new StrategyExecutor(Read(json));
        ClosedCandle first = History.Candle(1, 1);
        ClosedCandle second = History.Candle(2, 1);

        executor.Step("BTCUSDT", first, new FundingPayment(first.CloseTime, 0.0001m));
        StrategyStep order = executor.Step("BTCUSDT", second, new FundingPayment(second.CloseTime, 0.0006m));

        Assert.That((order.Entry, order.Size), Is.EqualTo(((EntrySide?)EntrySide.Short, (decimal?)50m)));
    }

    [Test]
    public void An_order_without_a_size_gives_no_plan()
    {
        var step = new StrategyStep(History.Start, EntrySide.Long, TimeSpan.FromHours(4), ExitDistance.None, ExitDistance.None) { Size = null };

        Assert.That(EntryPlan.Of(step, EntrySide.Long), Is.Null);
        Assert.That(EntryPlan.Of(step with { Size = 0 }, EntrySide.Long), Is.Null);
        Assert.That(EntryPlan.Of(step with { Size = 100 }, EntrySide.Long), Is.Not.Null);
    }

    [Test]
    public void A_cycle_outside_the_graph_is_refused_for_a_strategy_with_orders()
    {
        var steps = PairSteps.Of(Read(Graphs.Ladder), TimeSpan.FromHours(4), "BTCUSDT", [], []);

        Assert.That(() => steps.At(History.Start), Throws.InvalidOperationException.With.Message.Contains("by plan"));
    }

    private static TradingStrategy Read(string json)
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json);
        Assert.That(result.Errors, Is.Empty);
        return result.Value!;
    }
}
