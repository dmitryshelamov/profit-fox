using System.Text.RegularExpressions;

using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     Checks what a definition of a composite node means: names, kinds, settings, wires, values of inputs, cycles and outputs. Every
///     error is reported, not only the first.
/// </summary>
internal static partial class GraphValidator
{
    /// <summary>
    ///     The checked composite node, or null with the errors added to <paramref name="errors" />.
    /// </summary>
    public static CompositeNode? Validate(CompositeNodeDefinition definition, List<GraphError> errors)
    {
        int errorsBefore = errors.Count;
        CheckOutputs(definition, errors);
        Dictionary<string, Shape> shapes = CheckNodes(definition, errors);
        Dictionary<PinReference, WireDefinition> wired = CheckWires(definition, shapes, errors);

        // An input a wire goes to has a wire, even when that wire is wrong: its error is already reported.
        var wiredEnds = definition.Wires.Select(wire => wire.To).ToHashSet();
        CheckInputValues(shapes, wiredEnds, errors);
        CheckCycles(definition, shapes, wired, errors);

        return errors.Count == errorsBefore ? new CompositeNode(definition.Outputs, Order(definition, wired), definition.Wires) : null;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex Name();

    private static void CheckOutputs(CompositeNodeDefinition definition, List<GraphError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (PinDeclaration output in definition.Outputs)
        {
            if (!Name().IsMatch(output.Name))
            {
                errors.Add(new GraphError(GraphErrorCode.InvalidName, $"The output name {output.Name} is not a Latin letter followed by letters, digits or -.")
                {
                    Pin = output.Name
                });
            }
            else if (!names.Add(output.Name))
            {
                errors.Add(new GraphError(GraphErrorCode.DuplicateOutputName, $"Two outputs are named {output.Name}.") { Pin = output.Name });
            }
        }
    }

    // The pins of every node whose id and kind are valid; the first node of a repeated id wins.
    private static Dictionary<string, Shape> CheckNodes(CompositeNodeDefinition definition, List<GraphError> errors)
    {
        var shapes = new Dictionary<string, Shape>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int outputsNodes = 0;
        foreach (NodeDefinition node in definition.Nodes)
        {
            if (!Name().IsMatch(node.Id))
            {
                errors.Add(new GraphError(GraphErrorCode.InvalidName, $"The node id {node.Id} is not a Latin letter followed by letters, digits or -.")
                {
                    Node = node.Id
                });
                continue;
            }

            if (!ids.Add(node.Id))
            {
                errors.Add(new GraphError(GraphErrorCode.DuplicateNodeId, $"Two nodes have the id {node.Id}.") { Node = node.Id });
                continue;
            }

            if (node.Kind == NodeCatalog.OutputsKind)
            {
                outputsNodes++;
                CheckSettings(node, [], errors);
                shapes[node.Id] = new Shape(node, definition.Outputs, Outputs: [], IsOutputs: true);
                continue;
            }

            NodeKind? kind = NodeCatalog.Find(node.Kind);
            if (kind is null)
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownKind, $"The library has no node of kind {node.Kind}.") { Node = node.Id });
                continue;
            }

            CheckSettings(node, kind.Settings, errors);
            shapes[node.Id] = new Shape(node, kind.Inputs, kind.Outputs, IsOutputs: false);
        }

        if (outputsNodes != 1)
        {
            errors.Add(new GraphError(GraphErrorCode.OutputsNodeCount, $"A composite node has exactly one node of kind outputs, not {outputsNodes}."));
        }

        return shapes;
    }

    private static void CheckSettings(NodeDefinition node, IReadOnlyList<SettingDeclaration> declared, List<GraphError> errors)
    {
        foreach ((string name, string value) in node.Settings)
        {
            SettingDeclaration? setting = declared.FirstOrDefault(setting => setting.Name == name);
            if (setting is null)
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownSetting, $"A node of kind {node.Kind} has no setting {name}.")
                {
                    Node = node.Id,
                    Setting = name
                });
            }
            else if (!setting.Values.Contains(value, StringComparer.Ordinal))
            {
                errors.Add(new GraphError(GraphErrorCode.InvalidSettingValue, $"{value} is not a value of {name}; the values are {string.Join(", ", setting.Values)}.")
                {
                    Node = node.Id,
                    Setting = name
                });
            }
        }

        foreach (SettingDeclaration setting in declared.Where(setting => setting.Default is null && !node.Settings.ContainsKey(setting.Name)))
        {
            errors.Add(new GraphError(GraphErrorCode.MissingSetting, $"The setting {setting.Name} is not set.") { Node = node.Id, Setting = setting.Name });
        }
    }

    // The wire of every input that has one; wires to nodes of unknown kind are left unchecked.
    private static Dictionary<PinReference, WireDefinition> CheckWires(
        CompositeNodeDefinition definition,
        Dictionary<string, Shape> shapes,
        List<GraphError> errors)
    {
        var ids = definition.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var wired = new Dictionary<PinReference, WireDefinition>();
        foreach (WireDefinition wire in definition.Wires)
        {
            PinDeclaration? from = Resolve(wire.From, ids, shapes, shape => shape.Outputs, "output", errors);
            PinDeclaration? to = Resolve(wire.To, ids, shapes, shape => shape.Inputs, "input", errors);
            if (from is null || to is null)
            {
                continue;
            }

            if (from.Type != to.Type)
            {
                errors.Add(new GraphError(GraphErrorCode.TypeMismatch, $"The wire joins {wire.From} of type {Name(from.Type)} to {wire.To} of type {Name(to.Type)}.")
                {
                    Node = wire.To.Node,
                    Pin = wire.To.Pin
                });
            }
            else if (!wired.TryAdd(wire.To, wire))
            {
                errors.Add(new GraphError(GraphErrorCode.SecondWire, $"The input {wire.To} has a second wire, from {wire.From}.")
                {
                    Node = wire.To.Node,
                    Pin = wire.To.Pin
                });
            }
        }

        return wired;
    }

    private static PinDeclaration? Resolve(
        PinReference end,
        HashSet<string> ids,
        Dictionary<string, Shape> shapes,
        Func<Shape, IReadOnlyList<PinDeclaration>> pins,
        string side,
        List<GraphError> errors)
    {
        if (!ids.Contains(end.Node))
        {
            errors.Add(new GraphError(GraphErrorCode.UnknownNode, $"The wire goes to {end}, but the graph has no node {end.Node}.") { Node = end.Node });
            return null;
        }

        if (!shapes.TryGetValue(end.Node, out Shape? shape))
        {
            return null;
        }

        PinDeclaration? pin = pins(shape).FirstOrDefault(pin => pin.Name == end.Pin);
        if (pin is null)
        {
            errors.Add(new GraphError(GraphErrorCode.UnknownPin, $"The node {end.Node} has no {side} {end.Pin}.") { Node = end.Node, Pin = end.Pin });
        }

        return pin;
    }

    private static void CheckInputValues(Dictionary<string, Shape> shapes, HashSet<PinReference> wired, List<GraphError> errors)
    {
        foreach (Shape shape in shapes.Values)
        {
            NodeDefinition node = shape.Node;
            foreach ((string name, PinValue value) in node.Inputs)
            {
                PinDeclaration? input = shape.Inputs.FirstOrDefault(input => input.Name == name);
                if (input is null)
                {
                    errors.Add(new GraphError(GraphErrorCode.UnknownPin, $"The node {node.Id} has no input {name}.") { Node = node.Id, Pin = name });
                }
                else if (input.Type != value.Type)
                {
                    errors.Add(new GraphError(GraphErrorCode.TypeMismatch, $"The input {name} is of type {Name(input.Type)}, its value of type {Name(value.Type)}.")
                    {
                        Node = node.Id,
                        Pin = name
                    });
                }
                else if (wired.Contains(new PinReference(node.Id, name)))
                {
                    errors.Add(new GraphError(GraphErrorCode.ValueOnWiredInput, $"The input {name} has both a wire and a value.") { Node = node.Id, Pin = name });
                }
            }

            foreach (PinDeclaration input in shape.Inputs.Where(input => !wired.Contains(new PinReference(node.Id, input.Name))))
            {
                if (shape.IsOutputs)
                {
                    errors.Add(new GraphError(GraphErrorCode.UnwiredOutput, $"No wire goes to the output {input.Name}.") { Node = node.Id, Pin = input.Name });
                }
                else if (!node.Inputs.ContainsKey(input.Name))
                {
                    errors.Add(new GraphError(GraphErrorCode.MissingInputValue, $"The input {input.Name} has neither a wire nor a value.")
                    {
                        Node = node.Id,
                        Pin = input.Name
                    });
                }
            }
        }
    }

    private static void CheckCycles(
        CompositeNodeDefinition definition,
        Dictionary<string, Shape> shapes,
        Dictionary<PinReference, WireDefinition> wired,
        List<GraphError> errors)
    {
        Dictionary<string, List<string>> feeds = Feeds(wired);
        var finished = new Dictionary<string, bool>(StringComparer.Ordinal);
        List<string> path = [];
        foreach (NodeDefinition node in definition.Nodes.Where(node => shapes.ContainsKey(node.Id)))
        {
            Visit(node.Id);
        }

        return;

        // Depth first along the wires: a node met again while it is still on the path closes a cycle.
        void Visit(string id)
        {
            if (finished.TryGetValue(id, out bool done))
            {
                if (!done)
                {
                    var cycle = path.Skip(path.IndexOf(id)).Append(id).ToList();
                    errors.Add(new GraphError(GraphErrorCode.Cycle, $"The wires make a cycle: {string.Join(" → ", cycle)}.") { Node = id });
                }

                return;
            }

            finished[id] = false;
            path.Add(id);
            foreach (string next in feeds.GetValueOrDefault(id, []))
            {
                Visit(next);
            }

            path.RemoveAt(path.Count - 1);
            finished[id] = true;
        }
    }

    // The nodes in the order of the definition, each moved after the nodes that feed it.
    private static List<NodeDefinition> Order(CompositeNodeDefinition definition, Dictionary<PinReference, WireDefinition> wired)
    {
        var nodes = definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        Dictionary<string, List<string>> feeds = Feeds(wired);
        var waiting = definition.Nodes.ToDictionary(
            node => node.Id,
            node => wired.Keys.Count(input => input.Node == node.Id),
            StringComparer.Ordinal);

        List<NodeDefinition> order = [];
        var ready = new Queue<string>(definition.Nodes.Where(node => waiting[node.Id] == 0).Select(node => node.Id));
        while (ready.TryDequeue(out string? id))
        {
            order.Add(nodes[id]);
            foreach (string next in feeds.GetValueOrDefault(id, []))
            {
                waiting[next]--;
                if (waiting[next] == 0)
                {
                    ready.Enqueue(next);
                }
            }
        }

        return order;
    }

    // For each node, the nodes its outputs are wired to, once per wire.
    private static Dictionary<string, List<string>> Feeds(Dictionary<PinReference, WireDefinition> wired)
    {
        var feeds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (WireDefinition wire in wired.Values)
        {
            if (!feeds.TryGetValue(wire.From.Node, out List<string>? targets))
            {
                targets = [];
                feeds[wire.From.Node] = targets;
            }

            targets.Add(wire.To.Node);
        }

        return feeds;
    }

    private static string Name(PinType type)
    {
        return type == PinType.Number ? "number" : "boolean";
    }

    // The pins of a node: of its kind, or the declared outputs for the node of outputs.
    private sealed record Shape(NodeDefinition Node, IReadOnlyList<PinDeclaration> Inputs, IReadOnlyList<PinDeclaration> Outputs, bool IsOutputs);
}