using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Nodes;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Validation;

/// <summary>Checks that a graph can be executed, including every composite node inside it; returns every error at once.</summary>
public static class GraphValidator
{
    public static IReadOnlyList<GraphError> ValidateStrategy(StrategyDefinition definition, CompositeCatalog catalog)
    {
        var errors = new List<GraphError>();
        CheckParameters(definition.Parameters, errors);
        CheckGraph(definition.Graph, new PinContext(definition.Parameters, [], []), catalog, "", [], errors);

        return errors;
    }

    public static IReadOnlyList<GraphError> ValidateComposite(CompositeNodeId id, CompositeDefinition definition, CompositeCatalog catalog)
    {
        var errors = new List<GraphError>();
        CheckComposite(definition, catalog, "", [id], errors);

        return errors;
    }

    private static void CheckComposite(CompositeDefinition definition, CompositeCatalog catalog, string path, List<CompositeNodeId> stack, List<GraphError> errors)
    {
        Duplicates(definition.Inputs, "Input", path, errors);
        Duplicates(definition.Outputs, "Output", path, errors);
        int outputsNodes = definition.Graph.Nodes.Count(node => node.Type == new SimpleNodeType(SimpleNodeKind.Outputs));

        if (definition.Outputs.Count > 0 && outputsNodes != 1)
        {
            errors.Add(new GraphError(path, null, "Composite node needs exactly one outputs node."));
        }

        CheckGraph(definition.Graph, new PinContext([], definition.Inputs, definition.Outputs), catalog, path, stack, errors);
    }

    private static void Duplicates(IReadOnlyList<PinDeclaration> pins, string side, string path, List<GraphError> errors)
    {
        foreach (IGrouping<string, PinDeclaration> duplicate in pins.GroupBy(pin => pin.Name).Where(group => group.Count() > 1))
        {
            errors.Add(new GraphError(path, duplicate.Key, $"{side} is declared more than once."));
        }
    }

    private static void CheckGraph(Graph graph, PinContext context, CompositeCatalog catalog, string path, List<CompositeNodeId> stack, List<GraphError> errors)
    {
        int start = errors.Count;

        foreach (IGrouping<string, GraphNode> duplicate in graph.Nodes.GroupBy(node => node.Id).Where(group => group.Count() > 1))
        {
            errors.Add(new GraphError(duplicate.Key, null, "Node id is used more than once."));
        }

        var pins = graph.Nodes.DistinctBy(node => node.Id).ToDictionary(node => node.Id, node => PinsOf(node, context, catalog, errors));

        foreach (Wire wire in graph.Wires)
        {
            PinDeclaration? from = Find(pins, wire.From, node => node.Outputs, "output", errors);
            PinDeclaration? to = Find(pins, wire.To, node => node.Inputs, "input", errors);

            if (from is not null && to is not null && from.Type != to.Type)
            {
                errors.Add(new GraphError(
                    wire.To.Node,
                    wire.To.Pin,
                    $"Type {to.Type} does not match type {from.Type} of {wire.From.Node}.{wire.From.Pin}."));
            }
        }

        var wired = graph.Wires.Select(wire => wire.To).ToHashSet();

        foreach (GraphNode node in graph.Nodes.DistinctBy(node => node.Id))
        {
            CheckInputs(node, pins[node.Id], wired, errors);
        }

        foreach (IGrouping<PinRef, Wire> crowded in graph.Wires.GroupBy(wire => wire.To).Where(group => group.Count() > 1))
        {
            errors.Add(new GraphError(crowded.Key.Node, crowded.Key.Pin, "Input has more than one wire."));
        }

        IReadOnlyList<string> cycle = Cycle(graph.Wires);

        if (cycle.Count > 0)
        {
            errors.Add(new GraphError("", null, $"Wires form a cycle: {string.Join(", ", cycle)}."));
        }

        for (int index = start; index < errors.Count; index++)
        {
            errors[index] = errors[index] with { Path = Join(path, errors[index].Path) };
        }

        foreach (GraphNode node in graph.Nodes.DistinctBy(node => node.Id))
        {
            CheckNested(node, catalog, Join(path, node.Id), stack, errors);
        }
    }

    private static void CheckNested(GraphNode node, CompositeCatalog catalog, string path, List<CompositeNodeId> stack, List<GraphError> errors)
    {
        if (node.Type is not CompositeNodeType { Id: var id } || catalog.Find(id) is not { } definition)
        {
            return;
        }

        if (stack.Contains(id))
        {
            string chain = string.Join(" → ", stack.Append(id).Select(Describe));
            errors.Add(new GraphError(path, null, $"Composite node contains itself: {chain}."));

            return;
        }

        stack.Add(id);
        CheckComposite(definition, catalog, path, stack, errors);
        stack.RemoveAt(stack.Count - 1);
    }

    private static string Describe(CompositeNodeId id)
    {
        return ((CustomNodeId)id).Value.ToString();
    }

    private static string Join(string path, string tail)
    {
        return path.Length == 0 ? tail : tail.Length == 0 ? path : $"{path}/{tail}";
    }

    /// <summary>Nodes left after peeling off every node without incoming or without outgoing wires lie on a cycle.</summary>
    private static IReadOnlyList<string> Cycle(IReadOnlyList<Wire> wires)
    {
        var edges = wires.Select(wire => (From: wire.From.Node, To: wire.To.Node)).Distinct().ToList();
        bool peeled = true;

        while (peeled)
        {
            var sources = edges.Select(edge => edge.From).ToHashSet();
            var targets = edges.Select(edge => edge.To).ToHashSet();
            int before = edges.Count;
            edges.RemoveAll(edge => !targets.Contains(edge.From) || !sources.Contains(edge.To));
            peeled = edges.Count < before;
        }

        return [.. edges.Select(edge => edge.From).Distinct().Order(StringComparer.Ordinal)];
    }

    private static void CheckParameters(IReadOnlyList<StrategyParameter> parameters, List<GraphError> errors)
    {
        foreach (IGrouping<string, StrategyParameter> duplicate in parameters.GroupBy(parameter => parameter.Name).Where(group => group.Count() > 1))
        {
            errors.Add(new GraphError("", duplicate.Key, "Parameter is declared more than once."));
        }

        foreach (StrategyParameter parameter in parameters)
        {
            string? reason = parameter.Reject(parameter.Default);

            if (reason is not null)
            {
                errors.Add(new GraphError("", parameter.Name, $"Default value {reason}"));
            }
        }
    }

    private static void CheckInputs(GraphNode node, NodePins pins, HashSet<PinRef> wired, List<GraphError> errors)
    {
        foreach (PinDeclaration input in pins.Inputs)
        {
            bool hasValue = node.Inputs.TryGetValue(input.Name, out NodeValue? value);

            if (hasValue && value!.Type != input.Type)
            {
                errors.Add(new GraphError(node.Id, input.Name, $"Value does not match input type {input.Type}."));
            }
            else if (!hasValue && !wired.Contains(new PinRef(node.Id, input.Name)))
            {
                errors.Add(new GraphError(node.Id, input.Name, "Input is not connected and has no value."));
            }
        }

        foreach (string unknown in node.Inputs.Keys.Where(name => pins.Inputs.All(input => input.Name != name)))
        {
            errors.Add(new GraphError(node.Id, unknown, $"Node has no input {unknown}."));
        }
    }

    private static NodePins PinsOf(GraphNode node, PinContext context, CompositeCatalog catalog, List<GraphError> errors)
    {
        if (node.Type is SimpleNodeType { Kind: var kind })
        {
            return SimpleNodeCatalog.Pins(kind, node.Settings, context);
        }

        CompositeDefinition? composite = catalog.Find(((CompositeNodeType)node.Type).Id);

        if (composite is null)
        {
            errors.Add(new GraphError(node.Id, null, "No such composite node."));

            return new NodePins([], []);
        }

        return new NodePins(composite.Inputs, composite.Outputs);
    }

    private static PinDeclaration? Find(
        Dictionary<string, NodePins> pins,
        PinRef reference,
        Func<NodePins, IReadOnlyList<PinDeclaration>> side,
        string sideName,
        List<GraphError> errors)
    {
        if (!pins.TryGetValue(reference.Node, out NodePins? node))
        {
            errors.Add(new GraphError(reference.Node, null, "No such node."));

            return null;
        }

        PinDeclaration? pin = side(node).FirstOrDefault(declaration => declaration.Name == reference.Pin);

        if (pin is null)
        {
            errors.Add(new GraphError(reference.Node, reference.Pin, $"Node has no {sideName} {reference.Pin}."));
        }

        return pin;
    }
}