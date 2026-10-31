using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Nodes;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Replaces composite nodes of a valid strategy with their simple nodes and orders the nodes so that sources come first.</summary>
internal sealed class GraphExpander(CompositeCatalog catalog)
{
    private readonly Dictionary<string, int> _index = [];
    private readonly Dictionary<string, string> _outputsNodeOf = [];
    private readonly List<(Scope Scope, GraphNode Node, string Path)> _pending = [];

    public static IReadOnlyList<RunNode> Expand(StrategyDefinition definition, CompositeCatalog catalog)
    {
        var expander = new GraphExpander(catalog);
        var root = new Scope(definition.Graph, "", new PinContext(definition.Parameters, [], []), null, null);
        expander.Allocate(root);

        return expander.Order(expander._pending.Select(expander.Resolve).ToList());
    }

    private void Allocate(Scope scope)
    {
        foreach (GraphNode node in scope.Graph.Nodes)
        {
            string path = Join(scope.Prefix, node.Id);

            if (node.Type is SimpleNodeType)
            {
                _index[path] = _pending.Count;
                _pending.Add((scope, node, path));

                continue;
            }

            CompositeDefinition definition = catalog.Find(((CompositeNodeType)node.Type).Id)!;
            GraphNode outputs = definition.Graph.Nodes.FirstOrDefault(inner => inner.Type == new SimpleNodeType(SimpleNodeKind.Outputs))!;
            _outputsNodeOf[path] = Join(path, outputs?.Id ?? "");
            Allocate(new Scope(definition.Graph, path, new PinContext([], definition.Inputs, definition.Outputs), scope, node));
        }
    }

    private RunNode Resolve((Scope Scope, GraphNode Node, string Path) item)
    {
        (Scope scope, GraphNode node, string path) = item;
        SimpleNodeKind kind = ((SimpleNodeType)node.Type).Kind;
        NodePins pins = SimpleNodeCatalog.Pins(kind, node.Settings, scope.Context);
        Dictionary<string, Source> inputs = kind == SimpleNodeKind.Inputs
            ? pins.Outputs.ToDictionary(pin => pin.Name, pin => SourceOf(scope.Parent!, scope.Owner!, pin.Name))
            : pins.Inputs.ToDictionary(pin => pin.Name, pin => SourceOf(scope, node, pin.Name));

        return new RunNode(path, kind, node.Settings, inputs);
    }

    private Source SourceOf(Scope scope, GraphNode node, string pin)
    {
        Wire? wire = scope.Graph.Wires.FirstOrDefault(candidate => candidate.To == new PinRef(node.Id, pin));

        if (wire is null)
        {
            return new Source(node.Inputs[pin]);
        }

        string from = Join(scope.Prefix, wire.From.Node);

        return new Source(null, _index[_outputsNodeOf.GetValueOrDefault(from, from)], wire.From.Pin);
    }

    /// <summary>Topological order; among nodes that are ready at once, the smaller path goes first, so the order is stable.</summary>
    private IReadOnlyList<RunNode> Order(List<RunNode> nodes)
    {
        int[] waiting = nodes.Select(node => node.Inputs.Values.Count(source => source.Literal is null)).ToArray();
        var ready = new SortedSet<(string Path, int Index)>(
            nodes.Select((node, index) => (node.Path, index)).Where(entry => waiting[entry.index] == 0),
            Comparer<(string Path, int Index)>.Create((left, right) => string.CompareOrdinal(left.Path, right.Path)));
        var ordered = new List<int>();

        while (ready.Count > 0)
        {
            (string _, int current) = ready.Min;
            ready.Remove(ready.Min);
            ordered.Add(current);

            for (int index = 0; index < nodes.Count; index++)
            {
                foreach (Source _ in nodes[index].Inputs.Values.Where(source => source.Literal is null && source.Node == current))
                {
                    if (--waiting[index] == 0)
                    {
                        ready.Add((nodes[index].Path, index));
                    }
                }
            }
        }

        var position = ordered.Select((original, at) => (original, at)).ToDictionary(entry => entry.original, entry => entry.at);

        return
        [
            .. ordered.Select(original => nodes[original] with
            {
                Inputs = nodes[original].Inputs.ToDictionary(
                    input => input.Key,
                    input => input.Value.Literal is null ? input.Value with { Node = position[input.Value.Node] } : input.Value)
            })
        ];
    }

    private static string Join(string prefix, string id)
    {
        return prefix.Length == 0 ? id : $"{prefix}/{id}";
    }

    private sealed record Scope(Graph Graph, string Prefix, PinContext Context, Scope? Parent, GraphNode? Owner);
}