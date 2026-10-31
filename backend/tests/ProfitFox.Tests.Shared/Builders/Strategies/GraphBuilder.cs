using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Tests.Shared.Builders.Strategies;

/// <summary>Builds a graph for tests: nodes with input values, then wires written as <c>"node.pin"</c>.</summary>
public sealed class GraphBuilder
{
    private readonly Dictionary<string, NodePosition> _layout = [];
    private readonly List<GraphNode> _nodes = [];
    private readonly List<Wire> _wires = [];

    public GraphBuilder Node(string id, SimpleNodeKind kind, params (string Pin, NodeValue Value)[] inputs)
    {
        return Node(id, kind, new NodeSettings(), inputs);
    }

    public GraphBuilder Node(string id, SimpleNodeKind kind, NodeSettings settings, params (string Pin, NodeValue Value)[] inputs)
    {
        _nodes.Add(new GraphNode(id, new SimpleNodeType(kind), inputs.ToDictionary(input => input.Pin, input => input.Value), settings));

        return this;
    }

    public GraphBuilder Composite(string id, CompositeNodeId composite, params (string Pin, NodeValue Value)[] inputs)
    {
        _nodes.Add(new GraphNode(id, new CompositeNodeType(composite), inputs.ToDictionary(input => input.Pin, input => input.Value), new NodeSettings()));

        return this;
    }

    public GraphBuilder Wire(string from, string to)
    {
        _wires.Add(new Wire(Pin(from), Pin(to)));

        return this;
    }

    public GraphBuilder At(string node, decimal x, decimal y)
    {
        _layout[node] = new NodePosition(x, y);

        return this;
    }

    public Graph Build()
    {
        return new Graph(_nodes.ToList(), _wires.ToList(), new Dictionary<string, NodePosition>(_layout));
    }

    private static PinRef Pin(string reference)
    {
        string[] parts = reference.Split('.');

        return new PinRef(parts[0], parts[1]);
    }
}