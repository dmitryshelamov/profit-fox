using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Nodes;

namespace ProfitFox.Domain.Graph.Reading;

/// <summary>
///     Checks what a graph of the right form means (spec strategy-graph): parameters, outputs, nodes, their settings and
///     inputs, wires, cycles; then what makes it a signal or a strategy. Every error is reported, in the order of the graph.
/// </summary>
internal static class GraphValidator
{
    public static List<GraphError> Validate(GraphDefinition graph)
    {
        List<GraphError> errors = [];
        CheckParameters(graph, errors);
        CheckOutputs(graph, errors);
        Dictionary<string, (NodeDefinition Node, NodeKind Kind)> nodes = CheckNodes(graph, errors);
        HashSet<PinReference> wired = CheckWires(graph, nodes, errors);
        CheckMissingInputs(graph, nodes, wired, errors);
        CheckCycles(graph, nodes, errors);

        return errors;
    }

    public static List<GraphError> ValidateSignal(GraphDefinition graph)
    {
        List<GraphError> errors = Validate(graph);

        if (graph.Outputs.Count != 1)
        {
            errors.Add(new GraphError(GraphErrorCode.SignalOutputCount, $"A signal has exactly one output, not {graph.Outputs.Count}."));
        }
        else if (graph.Outputs[0].Type != PinType.Event)
        {
            errors.Add(new GraphError(GraphErrorCode.SignalOutputNotEvent, "The output of a signal is an event.") { Pin = graph.Outputs[0].Name });
        }

        // A signal reads only the market: the nodes of the position belong to a strategy.
        foreach (NodeDefinition node in graph.Nodes.Where(node => NodeCatalog.Find(node.Kind) is { AllowedInSignal: false }))
        {
            errors.Add(
                new GraphError(GraphErrorCode.SignalPositionNode, $"A signal places no orders: {node.Kind} belongs to a strategy.")
                {
                    Node = node.Id
                });
        }

        return errors;
    }

    /// <summary>
    ///     A strategy (spec strategy-graph «Стратегия»): no declared outputs and no <c>outputs</c> node, at least one
    ///     <c>enter</c>, exactly one <c>close-after</c>, at most one <c>stop-loss</c> and one <c>take-profit</c>.
    /// </summary>
    public static List<GraphError> ValidateStrategy(GraphDefinition graph)
    {
        List<GraphError> errors = Validate(graph);

        if (graph.Outputs.Count > 0)
        {
            errors.Add(new GraphError(GraphErrorCode.StrategyHasOutputs, "A strategy has no declared outputs."));
        }
        else
        {
            foreach (NodeDefinition node in graph.Nodes.Where(node => node.Kind == NodeCatalog.Outputs))
            {
                errors.Add(new GraphError(GraphErrorCode.StrategyHasOutputs, $"A strategy has no {NodeCatalog.Outputs} node.") { Node = node.Id });
            }
        }

        if (graph.Nodes.All(node => node.Kind != NodeCatalog.Enter))
        {
            errors.Add(new GraphError(GraphErrorCode.StrategyEntryMissing, $"A strategy needs an {NodeCatalog.Enter} node."));
        }

        if (graph.Nodes.All(node => node.Kind != NodeCatalog.CloseAfter))
        {
            errors.Add(new GraphError(GraphErrorCode.StrategyCloseAfterMissing, $"A strategy needs one {NodeCatalog.CloseAfter} node."));
        }

        // The position on a pair is one, so its stop, its target and its time are one each: the first node stays.
        var exits = new HashSet<string>(StringComparer.Ordinal);

        foreach (NodeDefinition node in graph.Nodes.Where(node => node.Kind is NodeCatalog.StopLoss or NodeCatalog.TakeProfit or NodeCatalog.CloseAfter))
        {
            if (!exits.Add(node.Kind))
            {
                errors.Add(
                    new GraphError(GraphErrorCode.StrategyExitRepeated, $"A strategy has one {node.Kind} node; this is another.")
                    {
                        Node = node.Id
                    });
            }
        }

        return errors;
    }

    private static void CheckParameters(GraphDefinition graph, List<GraphError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (ParameterDefinition parameter in graph.Parameters)
        {
            if (!names.Add(parameter.Name))
            {
                errors.Add(
                    new GraphError(GraphErrorCode.DuplicateParameter, $"The parameter {parameter.Name} is declared twice.")
                    {
                        Parameter = parameter.Name
                    });

                continue;
            }

            if (parameter.Type == PinType.Event || parameter.Value.Type != parameter.Type)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.ParameterTypeMismatch,
                        parameter.Type == PinType.Event
                            ? "A parameter cannot be an event."
                            : $"The value is {PinTypes.Name(parameter.Value.Type)}, the parameter is {PinTypes.Name(parameter.Type)}.")
                    {
                        Parameter = parameter.Name
                    });

                continue;
            }

            if (parameter.Minimum is null && parameter.Maximum is null)
            {
                continue;
            }

            if (parameter.Type != PinType.Number || parameter.Minimum > parameter.Maximum)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.InvalidParameterBounds,
                        parameter.Type != PinType.Number ? "Only a number has a minimum and a maximum." : "The minimum is above the maximum.")
                    {
                        Parameter = parameter.Name
                    });

                continue;
            }

            decimal value = parameter.Value.Number;

            if (value < parameter.Minimum || value > parameter.Maximum)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.ParameterOutOfBounds,
                        $"The value {value} is outside [{parameter.Minimum?.ToString() ?? "..."}, {parameter.Maximum?.ToString() ?? "..."}].")
                    {
                        Parameter = parameter.Name
                    });
            }
        }
    }

    private static void CheckOutputs(GraphDefinition graph, List<GraphError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (OutputDefinition output in graph.Outputs.Where(output => !names.Add(output.Name)))
        {
            errors.Add(new GraphError(GraphErrorCode.DuplicateOutput, $"The output {output.Name} is declared twice.") { Pin = output.Name });
        }

        int outputsNodes = graph.Nodes.Count(node => node.Kind == NodeCatalog.Outputs);
        int expected = graph.Outputs.Count > 0 ? 1 : 0;

        if (outputsNodes > 1 || outputsNodes < expected)
        {
            errors.Add(
                new GraphError(
                    GraphErrorCode.OutputsNodeCount,
                    graph.Outputs.Count > 0
                        ? $"A graph with outputs has one {NodeCatalog.Outputs} node, not {outputsNodes}."
                        : $"A graph has at most one {NodeCatalog.Outputs} node, not {outputsNodes}."));
        }
    }

    // The nodes that can be checked further: a valid id, the first with that id, and a known kind.
    private static Dictionary<string, (NodeDefinition Node, NodeKind Kind)> CheckNodes(GraphDefinition graph, List<GraphError> errors)
    {
        var nodes = new Dictionary<string, (NodeDefinition, NodeKind)>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (NodeDefinition node in graph.Nodes)
        {
            if (node.Id.Contains(value: '.', StringComparison.Ordinal))
            {
                errors.Add(new GraphError(GraphErrorCode.InvalidNodeId, "A node id has no dot.") { Node = node.Id });

                continue;
            }

            if (!ids.Add(node.Id))
            {
                // The first node with the id stays and is checked; wires to the id go to it.
                errors.Add(new GraphError(GraphErrorCode.DuplicateNodeId, $"The node id {node.Id} is used twice.") { Node = node.Id });

                continue;
            }

            NodeKind? kind = NodeCatalog.Find(node.Kind);

            if (kind is null)
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownNodeKind, $"There is no node kind {node.Kind}.") { Node = node.Id });

                continue;
            }

            CheckSettings(node, kind, errors);
            CheckValues(graph, node, kind, errors);
            nodes[node.Id] = (node, kind);
        }

        return nodes;
    }

    private static void CheckSettings(NodeDefinition node, NodeKind kind, List<GraphError> errors)
    {
        foreach (string name in node.Settings.Keys.Where(name => kind.Settings.All(setting => setting.Name != name)))
        {
            errors.Add(new GraphError(GraphErrorCode.UnknownSetting, $"{kind.Name} has no setting {name}.") { Node = node.Id, Setting = name });
        }

        foreach (SettingDeclaration setting in kind.Settings)
        {
            if (!node.Settings.TryGetValue(setting.Name, out string? value))
            {
                if (setting.Default is null)
                {
                    errors.Add(
                        new GraphError(GraphErrorCode.MissingSetting, $"The setting {setting.Name} is required.")
                        {
                            Node = node.Id,
                            Setting = setting.Name
                        });
                }
            }
            else if (!setting.Values.Contains(value, StringComparer.Ordinal))
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.InvalidSetting,
                        $"{setting.Name} is one of {string.Join(", ", setting.Values)}, not {value}.")
                    {
                        Node = node.Id,
                        Setting = setting.Name
                    });
            }
        }
    }

    private static void CheckValues(GraphDefinition graph, NodeDefinition node, NodeKind kind, List<GraphError> errors)
    {
        IReadOnlyList<PinDeclaration> inputs = kind.Inputs(graph);

        foreach ((string name, GraphValue value) in node.Inputs)
        {
            PinDeclaration? input = inputs.FirstOrDefault(pin => pin.Name == name);

            if (input is null)
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownInput, $"{kind.Name} has no input {name}.") { Node = node.Id, Pin = name });
            }
            else if (input.Type == PinType.Event)
            {
                errors.Add(new GraphError(GraphErrorCode.ValueNotAllowed, "An event input takes a wire, not a value.") { Node = node.Id, Pin = name });
            }
            else if (input.Type != value.Type)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.ValueTypeMismatch,
                        $"The value is {PinTypes.Name(value.Type)}, the input is {PinTypes.Name(input.Type)}.")
                    {
                        Node = node.Id,
                        Pin = name
                    });
            }
        }
    }

    // The inputs that have a wire.
    private static HashSet<PinReference> CheckWires(
        GraphDefinition graph,
        Dictionary<string, (NodeDefinition Node, NodeKind Kind)> nodes,
        List<GraphError> errors)
    {
        var wired = new HashSet<PinReference>();
        var allIds = new HashSet<string>(graph.Nodes.Select(node => node.Id), StringComparer.Ordinal);

        foreach (WireDefinition wire in graph.Wires)
        {
            PinDeclaration? from = Pin(wire.From, output: true, graph, nodes, allIds, wire, errors);
            PinDeclaration? to = Pin(wire.To, output: false, graph, nodes, allIds, wire, errors);

            if (from is null || to is null)
            {
                continue;
            }

            if (from.Type != to.Type)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.WireTypeMismatch,
                        $"The wire carries {PinTypes.Name(from.Type)} into an input of type {PinTypes.Name(to.Type)}.")
                    {
                        Wire = wire
                    });
            }

            if (!wired.Add(wire.To))
            {
                errors.Add(
                    new GraphError(GraphErrorCode.SecondWire, $"The input {wire.To} already has a wire.")
                    {
                        Node = wire.To.Node,
                        Pin = wire.To.Pin,
                        Wire = wire
                    });
            }
            else if (nodes[wire.To.Node].Node.Inputs.ContainsKey(wire.To.Pin))
            {
                errors.Add(
                    new GraphError(GraphErrorCode.WireAndValue, $"The input {wire.To} has both a wire and a value.")
                    {
                        Node = wire.To.Node,
                        Pin = wire.To.Pin,
                        Wire = wire
                    });
            }
        }

        return wired;
    }

    private static PinDeclaration? Pin(
        PinReference reference,
        bool output,
        GraphDefinition graph,
        Dictionary<string, (NodeDefinition Node, NodeKind Kind)> nodes,
        HashSet<string> allIds,
        WireDefinition wire,
        List<GraphError> errors)
    {
        if (!nodes.TryGetValue(reference.Node, out (NodeDefinition Node, NodeKind Kind) node))
        {
            // A node with its own error (unknown kind, repeated id) is not reported again for each of its wires.
            if (!allIds.Contains(reference.Node))
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownNode, $"There is no node {reference.Node}.") { Wire = wire });
            }

            return null;
        }

        IReadOnlyList<PinDeclaration> pins = output ? node.Kind.Outputs(graph) : node.Kind.Inputs(graph);
        PinDeclaration? pin = pins.FirstOrDefault(candidate => candidate.Name == reference.Pin);

        if (pin is null)
        {
            errors.Add(
                new GraphError(
                    output ? GraphErrorCode.UnknownOutputPin : GraphErrorCode.UnknownInput,
                    $"{node.Kind.Name} has no {(output ? "output" : "input")} {reference.Pin}.")
                {
                    Node = reference.Node,
                    Pin = reference.Pin,
                    Wire = wire
                });
        }

        return pin;
    }

    private static void CheckMissingInputs(
        GraphDefinition graph,
        Dictionary<string, (NodeDefinition Node, NodeKind Kind)> nodes,
        HashSet<PinReference> wired,
        List<GraphError> errors)
    {
        foreach (NodeDefinition definition in graph.Nodes)
        {
            if (!nodes.TryGetValue(definition.Id, out (NodeDefinition Node, NodeKind Kind) node) || !ReferenceEquals(node.Node, definition))
            {
                continue;
            }

            foreach (PinDeclaration input in node.Kind.Inputs(graph))
            {
                if (!wired.Contains(new PinReference(definition.Id, input.Name)) && !definition.Inputs.ContainsKey(input.Name))
                {
                    errors.Add(
                        new GraphError(
                            GraphErrorCode.MissingInput,
                            input.Type == PinType.Event ? "The input needs a wire." : "The input needs a wire or a value.")
                        {
                            Node = definition.Id,
                            Pin = input.Name
                        });
                }
            }
        }
    }

    private static void CheckCycles(GraphDefinition graph, Dictionary<string, (NodeDefinition Node, NodeKind Kind)> nodes, List<GraphError> errors)
    {
        Dictionary<string, List<string>> next = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (WireDefinition wire in graph.Wires.Where(wire => nodes.ContainsKey(wire.From.Node) && nodes.ContainsKey(wire.To.Node)))
        {
            next[wire.From.Node].Add(wire.To.Node);
        }

        // Depth-first search in the order of the nodes; a node on the current path seen again closes a cycle.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (NodeDefinition node in graph.Nodes.Where(node => nodes.ContainsKey(node.Id)))
        {
            Visit(node.Id);
        }

        return;

        void Visit(string id)
        {
            if (state.GetValueOrDefault(id) != 0)
            {
                return;
            }

            state[id] = 1;
            path.Add(id);

            foreach (string target in next[id])
            {
                if (state.GetValueOrDefault(target) == 1)
                {
                    List<string> cycle = path.Skip(path.IndexOf(target)).ToList();
                    errors.Add(
                        new GraphError(GraphErrorCode.Cycle, $"The wires go round through {string.Join(", ", cycle)}.")
                        {
                            Node = target,
                            Nodes = cycle
                        });
                }
                else
                {
                    Visit(target);
                }
            }

            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }
    }
}