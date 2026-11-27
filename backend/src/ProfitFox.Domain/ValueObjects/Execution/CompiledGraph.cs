using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.ValueObjects.Execution;

/// <summary>
///     A checked graph laid out for running: nodes in the order of their wires, every output a slot, every input either
///     a slot or a written value.
/// </summary>
internal sealed class CompiledGraph
{
    public GraphDefinition Graph { get; }

    /// <summary>
    ///     The nodes in an order where each comes after the nodes its wires come from.
    /// </summary>
    public IReadOnlyList<CompiledNode> Nodes { get; }

    public int SlotCount { get; }

    /// <summary>
    ///     Where each declared output of the graph is read from, in the order of the declaration.
    /// </summary>
    public IReadOnlyList<InputSource> Outputs { get; }

    private CompiledGraph(GraphDefinition graph, IReadOnlyList<CompiledNode> nodes, int slotCount, IReadOnlyList<InputSource> outputs)
    {
        Graph = graph;
        Nodes = nodes;
        SlotCount = slotCount;
        Outputs = outputs;
    }

    /// <summary>
    ///     Lays out a graph that passed the checks; anything else is a programming error.
    /// </summary>
    public static CompiledGraph Compile(GraphDefinition graph)
    {
        var byId = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        List<NodeDefinition> order = Order(graph, byId);

        var slots = new Dictionary<PinReference, int>();
        foreach (NodeDefinition node in order)
        {
            foreach (PinDeclaration output in Kind(node).Outputs(graph))
            {
                slots[new PinReference(node.Id, output.Name)] = slots.Count;
            }
        }

        // An input that takes several wires (the orders of the position) has no single source and is not read.
        var wires = graph.Wires.GroupBy(wire => wire.To).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().From);
        List<CompiledNode> nodes = [];
        List<InputSource> outputs = [];
        foreach (NodeDefinition node in order)
        {
            NodeKind kind = Kind(node);
            InputSource[] inputs = [.. kind.Inputs(graph).Select(input => Source(node, input, wires, slots))];
            int[] outputSlots = [.. kind.Outputs(graph).Select(output => slots[new PinReference(node.Id, output.Name)])];
            nodes.Add(new CompiledNode(node, kind, inputs, outputSlots));
            if (node.Kind == NodeCatalog.Outputs)
            {
                outputs.AddRange(inputs);
            }
        }

        return new CompiledGraph(graph, nodes, slots.Count, outputs);
    }

    private static InputSource Source(
        NodeDefinition node,
        PinDeclaration input,
        Dictionary<PinReference, PinReference> wires,
        Dictionary<PinReference, int> slots)
    {
        if (!input.Many && wires.TryGetValue(new PinReference(node.Id, input.Name), out PinReference? from))
        {
            return new InputSource(slots[from], Value: default);
        }

        return input.WireOnly ? new InputSource(Slot: -1, PinValue.Missing(input.Type)) : new InputSource(Slot: -1, PinValue.From(node.Inputs[input.Name]));
    }

    // Kahn's order; among nodes that are ready, the one written first goes first, so the order is the same on every run.
    // A wire into the orders of the position sets no order: the position is computed before the nodes that read it.
    private static List<NodeDefinition> Order(GraphDefinition graph, Dictionary<string, NodeDefinition> byId)
    {
        var waiting = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var next = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (WireDefinition wire in graph.Wires.Where(wire => !GraphValidator.IsFeedback(byId[wire.To.Node].Kind, wire.To.Pin)))
        {
            waiting[wire.To.Node]++;
            next[wire.From.Node].Add(wire.To.Node);
        }

        var position = graph.Nodes.Select((node, index) => (node.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        var ready = new SortedSet<int>(graph.Nodes.Where(node => waiting[node.Id] == 0).Select(node => position[node.Id]));
        List<NodeDefinition> order = [];
        while (ready.Count > 0)
        {
            int first = ready.Min;
            ready.Remove(first);
            NodeDefinition node = graph.Nodes[first];
            order.Add(node);
            foreach (string target in next[node.Id])
            {
                if (--waiting[target] == 0)
                {
                    ready.Add(position[target]);
                }
            }
        }

        if (order.Count != byId.Count)
        {
            throw new InvalidOperationException("The graph has a cycle; it must pass the checks before it runs.");
        }

        return order;
    }

    private static NodeKind Kind(NodeDefinition node)
    {
        return NodeCatalog.Find(node.Kind) ?? throw new InvalidOperationException($"Unknown node kind {node.Kind}.");
    }
}
