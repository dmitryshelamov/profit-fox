using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     Replaces each <c>signal</c> node of a checked strategy with the nodes of the graph of its version (spec strategy-graph
///     «Раскрытие узла сигнала»; change add-strategy-constructor, design.md, decision 3), so the executor gets a graph of
///     the kinds it always had. The expansion is the same for the same graph and the same versions.
/// </summary>
internal static class SignalExpansion
{
    /// <summary>
    ///     The ids the nodes of <paramref name="signal" /> get in place of the node <paramref name="node" />, in the order
    ///     of its graph: <c>&lt;node&gt;/&lt;id&gt;</c>, for every node but <c>parameters</c> and <c>outputs</c>.
    /// </summary>
    public static IEnumerable<string> ExpandedIds(string node, Signal signal)
    {
        return Inner(signal).Select(inner => Id(node, inner.Id));
    }

    /// <summary>
    ///     The graph without <c>signal</c> nodes. <paramref name="graph" /> passed the checks of a strategy with
    ///     <paramref name="signals" />; it is not changed.
    /// </summary>
    public static GraphDefinition Expand(GraphDefinition graph, SignalSet signals)
    {
        var expansions = new Dictionary<string, Expansion>(StringComparer.Ordinal);
        foreach (NodeDefinition node in graph.Nodes.Where(node => node.Kind == NodeCatalog.Signal))
        {
            SignalNodes.TryReference(node, out SignalReference reference);
            Signal signal = signals.Find(reference).Signal ?? throw new InvalidOperationException($"The signal of the node {node.Id} is not found.");
            expansions.Add(node.Id, new Expansion(node, signal, Event(node.Id, signal)));
        }

        if (expansions.Count == 0)
        {
            return graph;
        }

        // What comes into each input of a signal node by a wire of the strategy.
        var fed = graph.Wires
            .Where(wire => expansions.ContainsKey(wire.To.Node))
            .ToDictionary(wire => wire.To, wire => wire.From);

        List<NodeDefinition> nodes = [];
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (expansions.TryGetValue(node.Id, out Expansion? expansion))
            {
                nodes.AddRange(Inner(expansion.Signal).Select(inner => ExpandNode(expansion, inner, fed)));
            }
            else
            {
                nodes.Add(node);
            }
        }

        // The wires of the strategy in their order: those into a signal node drop out, those from its event start where
        // the event of the signal comes from. Then the inner wires of each signal node, in the order of its graph.
        List<WireDefinition> wires = [];
        foreach (WireDefinition wire in graph.Wires.Where(wire => !expansions.ContainsKey(wire.To.Node)))
        {
            wires.Add(expansions.TryGetValue(wire.From.Node, out Expansion? from) ? wire with { From = from.Event } : wire);
        }

        foreach (NodeDefinition node in graph.Nodes)
        {
            if (expansions.TryGetValue(node.Id, out Expansion? expansion))
            {
                wires.AddRange(InnerWires(expansion, fed));
            }
        }

        return new GraphDefinition(graph.Parameters, graph.Outputs, nodes, wires);
    }

    private static IEnumerable<NodeDefinition> Inner(Signal signal)
    {
        return signal.Graph.Nodes.Where(node => node.Kind is not (NodeCatalog.Parameters or NodeCatalog.Outputs));
    }

    private static string Id(string node, string inner)
    {
        return $"{node}/{inner}";
    }

    // Where the event of the signal comes from in the expanded graph: the output wired into its outputs node.
    private static PinReference Event(string node, Signal signal)
    {
        string outputs = signal.Graph.Nodes.Single(inner => inner.Kind == NodeCatalog.Outputs).Id;
        PinReference from = signal.Graph.Wires.Single(wire => wire.To.Node == outputs).From;
        return new PinReference(Id(node, from.Node), from.Pin);
    }

    // The inner node with its settings and written values; an input wired from a parameter of the signal gets what is
    // given to that input of the signal node: a wire (added among the inner wires), a written value, or the value of the
    // parameter in the version.
    private static NodeDefinition ExpandNode(Expansion expansion, NodeDefinition inner, Dictionary<PinReference, PinReference> fed)
    {
        var inputs = new Dictionary<string, GraphValue>(inner.Inputs, StringComparer.Ordinal);
        foreach ((WireDefinition wire, ParameterDefinition parameter) in ParameterWires(expansion.Signal).Where(item => item.Wire.To.Node == inner.Id))
        {
            if (fed.ContainsKey(new PinReference(expansion.Node.Id, parameter.Name)))
            {
                continue;
            }

            inputs[wire.To.Pin] = expansion.Node.Inputs.TryGetValue(parameter.Name, out GraphValue written) ? written : parameter.Value;
        }

        return new NodeDefinition(Id(expansion.Node.Id, inner.Id), inner.Kind, inner.Settings, inputs);
    }

    private static IEnumerable<WireDefinition> InnerWires(Expansion expansion, Dictionary<PinReference, PinReference> fed)
    {
        string parameters = ParametersNode(expansion.Signal);
        foreach (WireDefinition wire in expansion.Signal.Graph.Wires.Where(wire => wire.To.Node != Outputs(expansion.Signal)))
        {
            var to = new PinReference(Id(expansion.Node.Id, wire.To.Node), wire.To.Pin);
            if (wire.From.Node != parameters)
            {
                yield return new WireDefinition(new PinReference(Id(expansion.Node.Id, wire.From.Node), wire.From.Pin), to);
            }
            else if (fed.TryGetValue(new PinReference(expansion.Node.Id, wire.From.Pin), out PinReference? source))
            {
                yield return new WireDefinition(source, to);
            }
        }
    }

    private static IEnumerable<(WireDefinition Wire, ParameterDefinition Parameter)> ParameterWires(Signal signal)
    {
        string parameters = ParametersNode(signal);
        return signal.Graph.Wires
            .Where(wire => wire.From.Node == parameters)
            .Select(wire => (wire, signal.Graph.Parameters.First(parameter => parameter.Name == wire.From.Pin)));
    }

    // The id of the parameters node of the signal; a signal without one has no wires from it.
    private static string ParametersNode(Signal signal)
    {
        return signal.Graph.Nodes.FirstOrDefault(node => node.Kind == NodeCatalog.Parameters)?.Id ?? "";
    }

    private static string Outputs(Signal signal)
    {
        return signal.Graph.Nodes.Single(node => node.Kind == NodeCatalog.Outputs).Id;
    }

    private sealed record Expansion(NodeDefinition Node, Signal Signal, PinReference Event);
}
