using ProfitFox.Domain.Graph;
using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Graph.Reading;

namespace ProfitFox.Tests.Unit.Graph;

/// <summary>
///     Change add-strategy-graph, design.md, decision 8 (task 1.4): a strategy put together from a signal and the values
///     of a trade plan.
/// </summary>
public sealed class StrategyAssemblyTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void The_signal_of_H0001_with_a_short_for_4h_gives_the_strategy_of_H0001()
    {
        Signal signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

        TradingStrategy strategy = TradingStrategy.FromSignal(signal, EntrySide.Short, FourHours);

        string written = StrategyGraph.Write(strategy.Graph);
        Assert.That(written, Is.EqualTo(StrategyGraph.Write(StrategyGraph.Read(Graphs.H0001Strategy).Value!)));
        Assert.That(StrategyGraph.ReadStrategy(written).Errors, Is.Empty);
        Assert.That(strategy.Entries, Is.EqualTo(new[] { new StrategyEntry("enter", EntrySide.Short) }));
    }

    [Test]
    public void A_stop_and_a_target_add_their_nodes_with_these_values()
    {
        Signal signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

        TradingStrategy strategy = TradingStrategy.FromSignal(signal, EntrySide.Long, TimeSpan.FromDays(1), stop: 0.02m, target: 0.04m);

        Assert.That(
            strategy.Graph.Nodes.Select(node => (node.Id, node.Kind)),
            Is.EqualTo(
                new[]
                {
                    ("p", "parameters"),
                    ("rate", "funding-rate"),
                    ("high", "compare"),
                    ("first", "first-time"),
                    ("enter", "enter"),
                    ("close-after", "close-after"),
                    ("stop-loss", "stop-loss"),
                    ("take-profit", "take-profit")
                }));
        Assert.That(Node(strategy, "enter").Settings["side"], Is.EqualTo("long"));
        Assert.That(Node(strategy, "close-after").Inputs["after"], Is.EqualTo(GraphValue.FromDuration(TimeSpan.FromDays(1))));
        Assert.That(Node(strategy, "stop-loss").Inputs["distance"], Is.EqualTo(GraphValue.FromNumber(0.02m)));
        Assert.That(Node(strategy, "take-profit").Inputs["distance"], Is.EqualTo(GraphValue.FromNumber(0.04m)));
        Assert.That(StrategyGraph.ReadStrategy(StrategyGraph.Write(strategy.Graph)).Errors, Is.Empty);
    }

    [Test]
    public void Only_a_stop_adds_only_its_node()
    {
        Signal signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

        TradingStrategy strategy = TradingStrategy.FromSignal(signal, EntrySide.Short, FourHours, stop: 0.02m);

        Assert.That(strategy.Graph.Nodes.Select(node => node.Kind), Does.Contain("stop-loss").And.Not.Contain("take-profit"));
    }

    [Test]
    public void A_taken_id_gets_a_number()
    {
        // The signal of H0001 with its nodes named as the new nodes would be: «enter» and «enter-1» are taken.
        string json = Graphs.H0001
            .Replace("\"first\"", "\"enter\"", StringComparison.Ordinal)
            .Replace("first.", "enter.", StringComparison.Ordinal)
            .Replace("\"high\"", "\"enter-1\"", StringComparison.Ordinal)
            .Replace("high.", "enter-1.", StringComparison.Ordinal)
            .Replace("\"rate\"", "\"close-after\"", StringComparison.Ordinal)
            .Replace("rate.", "close-after.", StringComparison.Ordinal);
        ReadResult<Signal> signal = StrategyGraph.ReadSignal(json);
        Assert.That(signal.Errors, Is.Empty, "the renamed signal must pass the checks");

        TradingStrategy strategy = TradingStrategy.FromSignal(signal.Value!, EntrySide.Short, FourHours, stop: 0.02m);

        Assert.That(
            strategy.Graph.Nodes.Select(node => (node.Id, node.Kind)),
            Is.EqualTo(
                new[]
                {
                    ("p", "parameters"),
                    ("close-after", "funding-rate"),
                    ("enter-1", "compare"),
                    ("enter", "first-time"),
                    ("enter-2", "enter"),
                    ("close-after-1", "close-after"),
                    ("stop-loss", "stop-loss")
                }));
        Assert.That(strategy.Graph.Wires[^1], Is.EqualTo(new WireDefinition(new PinReference("enter", "event"), new PinReference("enter-2", "when"))));
        Assert.That(strategy.Entries, Is.EqualTo(new[] { new StrategyEntry("enter-2", EntrySide.Short) }));
    }

    [Test]
    public void The_graph_of_the_signal_does_not_change()
    {
        Signal signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;
        string before = StrategyGraph.Write(signal.Graph);

        TradingStrategy.FromSignal(signal, EntrySide.Short, FourHours, stop: 0.02m, target: 0.04m);

        Assert.That(StrategyGraph.Write(signal.Graph), Is.EqualTo(before));
        Assert.That(StrategyGraph.ReadSignal(before).Errors, Is.Empty);
    }

    [Test]
    public void Values_that_make_no_strategy_are_an_error_of_the_program()
    {
        Signal signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

        Assert.That(() => TradingStrategy.FromSignal(signal, (EntrySide)7, FourHours), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    private static NodeDefinition Node(TradingStrategy strategy, string id)
    {
        return strategy.Graph.Nodes.Single(node => node.Id == id);
    }
}
