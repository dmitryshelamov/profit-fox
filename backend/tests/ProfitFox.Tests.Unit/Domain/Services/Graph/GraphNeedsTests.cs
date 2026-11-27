using System.Text.Json;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Change add-idea-score, task 4.4 (design.md, decision 6): a graph needs the points of open interest only when the node
///     «OI» is in the graph the executor runs — a node inside a signal of a strategy counts, a strategy without one does not.
/// </summary>
public sealed class GraphNeedsTests
{
    private static readonly Guid OpenInterestUp = Guid.Parse("5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d");

    private const string OpenInterestSignal =
        """
        {
          "formatVersion": 1,
          "parameters": [{ "name": "threshold", "type": "number", "value": 1000000 }],
          "outputs": [{ "name": "entry", "type": "event" }],
          "nodes": [
            { "id": "p", "kind": "parameters" },
            { "id": "oi", "kind": "open-interest" },
            { "id": "ge", "kind": "compare", "settings": { "operation": "ge" } },
            { "id": "first", "kind": "first-time" },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [
            { "from": "oi.value", "to": "ge.a" },
            { "from": "p.threshold", "to": "ge.b" },
            { "from": "ge.result", "to": "first.value" },
            { "from": "first.event", "to": "out.entry" }
          ]
        }
        """;

    [Test]
    public void A_graph_without_the_node_needs_no_open_interest()
    {
        Assert.That(GraphNeeds.OpenInterest(StrategyGraph.ReadSignal(Graphs.H0001).Value!.Graph), Is.False);
        Assert.That(GraphNeeds.OpenInterest(StrategyGraph.ReadStrategy(SignalLibrary.Strategy(), SignalLibrary.Set).Value!.Graph), Is.False);
    }

    [Test]
    public void A_graph_with_the_node_needs_it()
    {
        Assert.That(GraphNeeds.OpenInterest(StrategyGraph.ReadSignal(OpenInterestSignal).Value!.Graph), Is.True);
    }

    [Test]
    public void A_strategy_with_the_node_only_inside_its_signal_needs_it()
    {
        using var document = JsonDocument.Parse(OpenInterestSignal);
        GraphDefinition form = StrategyGraph.ReadForm(document.RootElement).Value!;
        var signals = new SignalSet([new SignalVersion(new SignalReference(OpenInterestUp, 1), "oi-up-4h", LatestVersion: 1, form)]);
        string strategy = SignalLibrary.Strategy().Replace(SignalLibrary.FundingHigh.ToString(), OpenInterestUp.ToString(), StringComparison.Ordinal);

        ReadResult<TradingStrategy> read = StrategyGraph.ReadStrategy(strategy, signals);

        Assert.That(read.Errors, Is.Empty);
        Assert.That(read.Value!.Source.Nodes.Select(node => node.Kind), Has.None.EqualTo("open-interest"), "the strategy as written has a signal node only");
        Assert.That(GraphNeeds.OpenInterest(read.Value.Graph), Is.True);
    }
}
