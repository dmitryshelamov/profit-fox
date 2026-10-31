using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.BuiltIns;

/// <summary>Writes a built-in graph compactly: nodes with input values, then wires as <c>"node.pin"</c>.</summary>
internal sealed class GraphSketch
{
    private readonly List<GraphNode> _nodes = [];
    private readonly List<Wire> _wires = [];

    public GraphSketch Node(string id, SimpleNodeKind kind, NodeSettings? settings = null, params (string Pin, NodeValue Value)[] inputs)
    {
        _nodes.Add(new GraphNode(id, new SimpleNodeType(kind), inputs.ToDictionary(input => input.Pin, input => input.Value), settings ?? new NodeSettings()));

        return this;
    }

    public GraphSketch Wire(string from, string to)
    {
        _wires.Add(new Wire(Pin(from), Pin(to)));

        return this;
    }

    public Graph Build()
    {
        return new Graph(_nodes, _wires, new Dictionary<string, NodePosition>());
    }

    private static PinRef Pin(string reference)
    {
        string[] parts = reference.Split('.');

        return new PinRef(parts[0], parts[1]);
    }
}