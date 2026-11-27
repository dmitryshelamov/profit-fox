using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Spec strategy-graph «Стратегия», «Сторона входа» and «Сигнал» (change add-strategy-graph, tasks 1.2 and 1.3): what
///     makes a graph a strategy, the side of its entries read without running it, and that a signal places no orders.
/// </summary>
public sealed class StrategyValidationTests
{
    [Test]
    public void The_strategy_of_H0001_passes_the_checks()
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(Graphs.H0001Strategy);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Value!.Graph.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "p", "rate", "high", "first", "enter", "position", "close-after" }));
    }

    [Test]
    public void A_strategy_without_an_enter_node_is_refused()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveNodeAndWires("enter");
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyEntryMissing));
        Assert.That(error.Reason, Does.Contain("узел входа"));
    }

    [Test]
    public void A_strategy_without_any_exit_is_refused()
    {
        string json = Graphs.ChangedStrategy(graph => graph.RemoveNodeAndWires("close-after"));

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyExitMissing));
        Assert.That(error.Reason, Does.Contain("нужен выход"));
    }

    [Test]
    public void A_second_stop_loss_is_reported_on_its_node()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddExit("stop", "stop-loss", "distance", JsonNode.Parse("0.02")!);
            graph.AddExit("stop-again", "stop-loss", "distance", JsonNode.Parse("0.03")!);
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyExitRepeated));
        Assert.That(error.Node, Is.EqualTo("stop-again"));
    }

    [Test]
    public void A_second_take_profit_and_a_second_close_after_are_reported_too()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddExit("target", "take-profit", "distance", JsonNode.Parse("0.04")!);
            graph.AddExit("target-again", "take-profit", "distance", JsonNode.Parse("0.05")!);
            graph.AddExit("later", "close-after", "after", "1d");
        });

        List<GraphError> errors = Errors(json);

        Assert.That(
            errors.Select(error => (error.Code, error.Node)),
            Is.EqualTo(new[] { (GraphErrorCode.StrategyExitRepeated, "target-again"), (GraphErrorCode.StrategyExitRepeated, (string?)"later") }));
    }

    [Test]
    public void A_strategy_with_a_declared_output_is_refused()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph["outputs"]!.AsArray().Add(new JsonObject { ["name"] = "entry", ["type"] = "event" });
            graph.AddNode("out", "outputs");
            graph.AddWire("first.event", "out.entry");
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyHasOutputs));
        Assert.That(error.Reason, Does.Contain("no declared outputs"));
    }

    [Test]
    public void A_strategy_with_an_outputs_node_is_refused_even_without_declared_outputs()
    {
        string json = Graphs.ChangedStrategy(graph => graph.AddNode("out", "outputs"));

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyHasOutputs));
        Assert.That(error.Node, Is.EqualTo("out"));
    }

    [Test]
    public void An_enter_node_without_a_side_is_reported_on_its_setting()
    {
        string json = Graphs.ChangedStrategy(graph => graph.Node("enter").Remove("settings"));

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.MissingSetting));
        Assert.That((error.Node, error.Setting), Is.EqualTo(("enter", "side")));
    }

    [Test]
    public void A_side_that_is_not_long_or_short_is_reported_on_its_setting()
    {
        string json = Graphs.ChangedStrategy(graph => graph.Node("enter")["settings"]!["side"] = "both");

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.InvalidSetting));
        Assert.That((error.Node, error.Setting), Is.EqualTo(("enter", "side")));
    }

    [Test]
    public void The_side_is_read_without_running_the_strategy()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(Graphs.H0001Strategy).Value!;

        Assert.That(strategy.Entries, Is.EqualTo(new[] { new StrategyEntry("enter", EntrySide.Short) }));
    }

    [Test]
    public void A_strategy_may_enter_long_on_one_event_and_short_on_another()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.Nodes().Add(new JsonObject
            {
                ["id"] = "low",
                ["kind"] = "compare",
                ["settings"] = new JsonObject { ["operation"] = "lt" },
                ["inputs"] = new JsonObject { ["b"] = 0 }
            });
            graph.AddNode("first-low", "first-time");
            graph.AddEnter("enter-long", "long");
            graph.AddWire("rate.value", "low.a");
            graph.AddWire("low.result", "first-low.value");
            graph.AddWire("first-low.event", "enter-long.when");
        });

        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(
            result.Value!.Entries,
            Is.EqualTo(new[] { new StrategyEntry("enter", EntrySide.Short), new StrategyEntry("enter-long", EntrySide.Long) }));
    }

    [Test]
    public void Every_error_of_a_strategy_is_reported_at_once()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveWire("high.b");
            graph.RemoveNodeAndWires("close-after");
            graph.AddExit("stop", "stop-loss", "distance", JsonNode.Parse("0.02")!);
            graph.AddExit("stop-again", "stop-loss", "distance", JsonNode.Parse("0.03")!);
            graph.AddNode("out", "outputs");
            graph.RemoveWire("enter.order", "position.orders");
        });

        List<GraphError> errors = Errors(json);

        Assert.That(
            errors.Select(error => error.Code),
            Is.EquivalentTo(
                new[]
                {
                    GraphErrorCode.MissingInput,
                    GraphErrorCode.StrategyHasOutputs,
                    GraphErrorCode.StrategyOrderUnwired,
                    GraphErrorCode.StrategyExitRepeated
                }));
    }

    [Test]
    public void A_strategy_that_is_not_JSON_is_refused()
    {
        Assert.That(StrategyGraph.ReadStrategy("{").Errors.Single().Code, Is.EqualTo(GraphErrorCode.NotJson));
    }

    [Test]
    public void An_enter_node_in_a_signal_is_reported_on_its_node()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.AddEnter("enter", "short", intoPosition: false);
            graph.AddWire("first.event", "enter.when");
        });

        ReadResult<Signal> result = StrategyGraph.ReadSignal(json);

        GraphError error = result.Errors.Single();
        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.SignalPositionNode));
        Assert.That(error.Node, Is.EqualTo("enter"));
        Assert.That(error.Reason, Does.Contain("places no orders"));
        Assert.That(StrategyGraph.Read(json).IsValid, Is.True, "the node is fine for a graph that is not a signal");
    }

    [TestCase("stop-loss", "distance", "0.02")]
    [TestCase("take-profit", "distance", "0.04")]
    [TestCase("close-after", "after", "\"4h\"")]
    public void An_exit_node_in_a_signal_is_reported_on_its_node(string kind, string input, string value)
    {
        string json = Graphs.Changed(graph => graph.AddNode("exit", kind, input, JsonNode.Parse(value)!));

        GraphError error = StrategyGraph.ReadSignal(json).Errors.Single(error => error.Code != GraphErrorCode.MissingInput);

        Assert.That((error.Code, error.Node), Is.EqualTo((GraphErrorCode.SignalPositionNode, "exit")), "and its position has no wire");
    }

    private static List<GraphError> Errors(string json)
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json);
        Assert.That(result.IsValid, Is.False, "the strategy was expected to have errors");

        return [.. result.Errors];
    }
}
