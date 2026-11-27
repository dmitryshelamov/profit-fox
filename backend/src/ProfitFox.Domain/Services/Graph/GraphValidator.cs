using System.Globalization;

using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     Checks what a graph of the right form means (spec strategy-graph): parameters, outputs, nodes, their settings and
///     inputs, wires, cycles; then what makes it a signal or a strategy. Every error is reported, in the order of the graph.
///     The pins of a <c>signal</c> node come from the set of saved signals the check is given; a graph checked without a
///     set has no saved signals, so each of its <c>signal</c> nodes is an unknown signal.
/// </summary>
internal static class GraphValidator
{
    public static List<GraphError> Validate(GraphDefinition graph)
    {
        return Validate(graph, SignalSet.Empty);
    }

    /// <summary>
    ///     <paramref name="signals" /> null: the <c>signal</c> nodes are not looked up, for a signal, which refuses them by
    ///     their kind.
    /// </summary>
    public static List<GraphError> Validate(GraphDefinition graph, SignalSet? signals)
    {
        List<GraphError> errors = [];
        CheckParameters(graph, errors);
        CheckOutputs(graph, errors);
        Dictionary<string, CheckedNode> nodes = CheckNodes(graph, signals, errors);
        CheckExpandedIds(graph, nodes, errors);
        HashSet<PinReference> wired = CheckWires(graph, nodes, errors);
        CheckMissingInputs(graph, nodes, wired, errors);
        CheckCycles(graph, nodes, errors);

        return errors;
    }

    public static List<GraphError> ValidateSignal(GraphDefinition graph)
    {
        List<GraphError> errors = Validate(graph, signals: null);

        if (graph.Outputs.Count != 1)
        {
            errors.Add(new GraphError(GraphErrorCode.SignalOutputCount, $"A signal has exactly one output, not {graph.Outputs.Count}."));
        }
        else if (graph.Outputs[0].Type is not (PinType.Event or PinType.Boolean))
        {
            errors.Add(new GraphError(GraphErrorCode.SignalOutputNotEvent, "The output of a signal is an event or a boolean.") { Pin = graph.Outputs[0].Name });
        }

        // A signal reads only the market: the nodes of the position belong to a strategy, and so do other signals.
        foreach (NodeDefinition node in graph.Nodes.Where(node => NodeCatalog.Find(node.Kind) is { AllowedInSignal: false }))
        {
            errors.Add(
                node.Kind == NodeCatalog.Signal
                    ? new GraphError(GraphErrorCode.SignalInSignal, "A signal does not point at other signals: a signal node belongs to a strategy.")
                    {
                        Node = node.Id
                    }
                    : new GraphError(GraphErrorCode.SignalPositionNode, $"A signal places no orders: {node.Kind} belongs to a strategy.")
                    {
                        Node = node.Id
                    });
        }

        return errors;
    }

    /// <summary>
    ///     A strategy (spec strategy-graph «Стратегия», «Узел позиции в стратегии»): no declared outputs and no
    ///     <c>outputs</c> node, exactly one <c>position</c>, a node that can open the position and one that can close it,
    ///     every order wired into the position, at most one <c>stop-loss</c>, one <c>take-profit</c> and one
    ///     <c>close-after</c>.
    /// </summary>
    public static List<GraphError> ValidateStrategy(GraphDefinition graph)
    {
        return ValidateStrategy(graph, SignalSet.Empty);
    }

    /// <summary>
    ///     A strategy whose <c>signal</c> nodes point at versions of <paramref name="signals" /> (change
    ///     add-strategy-constructor, design.md, decision 2).
    /// </summary>
    public static List<GraphError> ValidateStrategy(GraphDefinition graph, SignalSet signals)
    {
        return ValidateStrategy(graph, signals, StrategyKind.Bot);
    }

    /// <summary>
    ///     A strategy of <paramref name="kind" /> (spec strategy-graph «Вид стратегии»): the checks of a strategy, then
    ///     the rules of its kind (<see cref="StrategyKindRules" />), each broken rule an error of its own.
    /// </summary>
    public static List<GraphError> ValidateStrategy(GraphDefinition graph, SignalSet signals, StrategyKind kind)
    {
        List<GraphError> errors = ValidateAnyStrategy(graph, signals);
        errors.AddRange(StrategyKindRules.Check(graph, kind));
        return errors;
    }

    // The checks every strategy passes, whatever its kind.
    private static List<GraphError> ValidateAnyStrategy(GraphDefinition graph, SignalSet signals)
    {
        List<GraphError> errors = Validate(graph, signals);

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

        CheckPosition(graph, errors);

        if (!graph.Nodes.Any(node => node.Kind is NodeCatalog.Enter or NodeCatalog.MarketOrder or NodeCatalog.LimitOrder))
        {
            errors.Add(
                new GraphError(
                    GraphErrorCode.StrategyEntryMissing,
                    $"Стратегии нужен узел входа: {NodeCatalog.Enter}, {NodeCatalog.MarketOrder} или {NodeCatalog.LimitOrder}."));
        }

        if (!graph.Nodes.Any(node => node.Kind is NodeCatalog.StopLoss or NodeCatalog.TakeProfit or NodeCatalog.CloseAfter
                                         or NodeCatalog.ClosePosition or NodeCatalog.MarketOrder or NodeCatalog.LimitOrder))
        {
            errors.Add(
                new GraphError(
                    GraphErrorCode.StrategyExitMissing,
                    "Стратегии нужен выход: узел, который закрывает позицию, — стоп, цель, срок, закрытие позиции, рыночная или лимитная заявка."));
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

    // Exactly one position, and every node with an output of an order wired by it into the position.
    private static void CheckPosition(GraphDefinition graph, List<GraphError> errors)
    {
        NodeDefinition[] positions = [.. graph.Nodes.Where(node => node.Kind == NodeCatalog.Position)];
        if (positions.Length == 0)
        {
            errors.Add(new GraphError(GraphErrorCode.StrategyPositionMissing, "Стратегии нужен узел позиции."));
        }

        foreach (NodeDefinition node in positions.Skip(1))
        {
            errors.Add(new GraphError(GraphErrorCode.StrategyPositionRepeated, "У стратегии один узел позиции; это второй.") { Node = node.Id });
        }

        var ids = new HashSet<string>(positions.Select(node => node.Id), StringComparer.Ordinal);
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (NodeCatalog.Find(node.Kind) is not { } kind || kind.Outputs(graph).All(pin => pin.Type != PinType.Order))
            {
                continue;
            }

            bool wired = graph.Wires.Any(wire => wire.From.Node == node.Id && wire.From.Pin == NodeCatalog.Order
                                                 && ids.Contains(wire.To.Node) && wire.To.Pin == NodeCatalog.Orders);
            if (!wired)
            {
                errors.Add(
                    new GraphError(GraphErrorCode.StrategyOrderUnwired, $"Заявка узла {node.Id} не подключена к позиции: нужен провод на {NodeCatalog.Position}.{NodeCatalog.Orders}.")
                    {
                        Node = node.Id,
                        Pin = NodeCatalog.Order
                    });
            }
        }
    }

    /// <summary>
    ///     Whether a wire into this input closes no cycle: the input <c>orders</c> of the node <c>position</c> (change
    ///     add-position-node, design.md, decision 2). The outputs of the position do not depend on the orders of the same
    ///     computation, so neither the search for cycles nor the order of computation counts these wires.
    /// </summary>
    public static bool IsFeedback(string kind, string pin)
    {
        return kind == NodeCatalog.Position && pin == NodeCatalog.Orders;
    }

    /// <summary>
    ///     The checks of the parameters of a graph only: names, types, bounds and the values within them.
    /// </summary>
    public static void CheckParameters(GraphDefinition graph, List<GraphError> errors)
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

            if (parameter.Type is PinType.Event or PinType.Order or PinType.Position || parameter.Value.Type != parameter.Type)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.ParameterTypeMismatch,
                        parameter.Type is PinType.Event or PinType.Order or PinType.Position
                            ? $"A parameter cannot be {PinTypes.Name(parameter.Type)}."
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
                        OutOfBounds(value, parameter.Minimum, parameter.Maximum))
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

    // The nodes that can be checked further: a valid id, the first with that id, and a known kind; each with its pins.
    private static Dictionary<string, CheckedNode> CheckNodes(GraphDefinition graph, SignalSet? signals, List<GraphError> errors)
    {
        var nodes = new Dictionary<string, CheckedNode>(StringComparer.Ordinal);
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

            bool settingsRight = CheckSettings(node, kind, errors);
            CheckedNode checkedNode = node.Kind == NodeCatalog.Signal
                ? CheckSignal(graph, node, kind, settingsRight, signals, errors)
                : new CheckedNode(node, kind, kind.Inputs(node, graph, SignalSet.Empty), kind.Outputs(node, graph, SignalSet.Empty), Signal: null);
            CheckValues(checkedNode, errors);
            nodes[node.Id] = checkedNode;
        }

        return nodes;
    }

    // A signal node whose version is found has the parameters of the version as inputs; one that is not found has its
    // own error and inputs nobody knows, which are then not checked.
    private static CheckedNode CheckSignal(
        GraphDefinition graph,
        NodeDefinition node,
        NodeKind kind,
        bool settingsRight,
        SignalSet? signals,
        List<GraphError> errors)
    {
        IReadOnlyList<PinDeclaration> outputs = kind.Outputs(node, graph, SignalSet.Empty);
        if (!settingsRight || signals is null || !SignalNodes.TryReference(node, out SignalReference reference))
        {
            return new CheckedNode(node, kind, Inputs: null, outputs, Signal: null);
        }

        SignalLookup found = signals.Find(reference);
        if (found.Signal is null)
        {
            errors.Add(
                new GraphError(found.Problem ?? GraphErrorCode.UnknownSignal, found.Reason ?? "The signal is not found.")
                {
                    Node = node.Id,
                    Setting = found.Name is null ? NodeCatalog.SignalSetting : NodeCatalog.VersionSetting
                });

            return new CheckedNode(node, kind, Inputs: null, outputs, Signal: null);
        }

        return new CheckedNode(node, kind, kind.Inputs(node, graph, signals), kind.Outputs(node, graph, signals), found.Signal);
    }

    // The ids an expansion gives its nodes (<signal node>/<node>) must be free in the graph and among other expansions.
    private static void CheckExpandedIds(GraphDefinition graph, Dictionary<string, CheckedNode> nodes, List<GraphError> errors)
    {
        var taken = new HashSet<string>(graph.Nodes.Select(node => node.Id), StringComparer.Ordinal);
        foreach (NodeDefinition definition in graph.Nodes)
        {
            if (!nodes.TryGetValue(definition.Id, out CheckedNode? node) || !ReferenceEquals(node.Node, definition) || node.Signal is null)
            {
                continue;
            }

            foreach (string id in SignalExpansion.ExpandedIds(definition.Id, node.Signal).Where(id => !taken.Add(id)))
            {
                errors.Add(
                    new GraphError(GraphErrorCode.ExpandedIdTaken, $"The id {id} an expanded node of the signal gets is taken.")
                    {
                        Node = definition.Id
                    });
            }
        }
    }

    // Whether every setting of the node is known and right.
    private static bool CheckSettings(NodeDefinition node, NodeKind kind, List<GraphError> errors)
    {
        int before = errors.Count;
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
            else if (setting.Free)
            {
                if (kind.CheckFreeSetting(setting.Name, value) is { } reason)
                {
                    errors.Add(new GraphError(GraphErrorCode.InvalidSetting, reason) { Node = node.Id, Setting = setting.Name });
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

        return errors.Count == before;
    }

    private static void CheckValues(CheckedNode node, List<GraphError> errors)
    {
        if (node.Inputs is not { } inputs)
        {
            return;
        }

        foreach ((string name, GraphValue value) in node.Node.Inputs)
        {
            PinDeclaration? input = inputs.FirstOrDefault(pin => pin.Name == name);

            if (input is null)
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownInput, $"{node.Kind.Name} has no input {name}.") { Node = node.Node.Id, Pin = name });
            }
            else if (input.WireOnly)
            {
                errors.Add(
                    new GraphError(GraphErrorCode.ValueNotAllowed, $"An input of type {PinTypes.Name(input.Type)} takes a wire, not a value.")
                    {
                        Node = node.Node.Id,
                        Pin = name
                    });
            }
            else if (input.Type != value.Type)
            {
                errors.Add(
                    new GraphError(
                        GraphErrorCode.ValueTypeMismatch,
                        $"The value is {PinTypes.Name(value.Type)}, the input is {PinTypes.Name(input.Type)}.")
                    {
                        Node = node.Node.Id,
                        Pin = name
                    });
            }
            else if (node.Kind.CheckValue(name, value) is { } reason)
            {
                errors.Add(new GraphError(GraphErrorCode.ParameterOutOfBounds, reason) { Node = node.Node.Id, Pin = name });
            }
            else if (node.Signal is not null)
            {
                CheckBounds(node, name, value, errors);
            }
        }
    }

    // A value written on an input of a signal node stays within the bounds of the parameter of its version (spec
    // strategy-graph «Границы параметра на входе узла сигнала»).
    private static void CheckBounds(CheckedNode node, string name, GraphValue value, List<GraphError> errors)
    {
        ParameterDefinition parameter = node.Signal!.Graph.Parameters.First(parameter => parameter.Name == name);
        decimal number = value.Number;
        bool inside = (parameter.Minimum is not { } minimum || number >= minimum) && (parameter.Maximum is not { } maximum || number <= maximum);
        if (value.Type != PinType.Number || inside)
        {
            return;
        }

        errors.Add(
            new GraphError(
                GraphErrorCode.ParameterOutOfBounds,
                OutOfBounds(value.Number, parameter.Minimum, parameter.Maximum))
            {
                Node = node.Node.Id,
                Pin = name
            });
    }

    // The inputs that have a wire.
    private static HashSet<PinReference> CheckWires(
        GraphDefinition graph,
        Dictionary<string, CheckedNode> nodes,
        List<GraphError> errors)
    {
        var wired = new HashSet<PinReference>();
        var allIds = new HashSet<string>(graph.Nodes.Select(node => node.Id), StringComparer.Ordinal);

        foreach (WireDefinition wire in graph.Wires)
        {
            PinDeclaration? from = Pin(wire.From, output: true, nodes, allIds, wire, errors);
            PinDeclaration? to = Pin(wire.To, output: false, nodes, allIds, wire, errors);

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

            bool first = wired.Add(wire.To);
            if (!first && !to.Many)
            {
                errors.Add(
                    new GraphError(GraphErrorCode.SecondWire, $"The input {wire.To} already has a wire.")
                    {
                        Node = wire.To.Node,
                        Pin = wire.To.Pin,
                        Wire = wire
                    });
            }
            else if (first && nodes[wire.To.Node].Node.Inputs.ContainsKey(wire.To.Pin))
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
        Dictionary<string, CheckedNode> nodes,
        HashSet<string> allIds,
        WireDefinition wire,
        List<GraphError> errors)
    {
        if (!nodes.TryGetValue(reference.Node, out CheckedNode? node))
        {
            // A node with its own error (unknown kind, repeated id) is not reported again for each of its wires.
            if (!allIds.Contains(reference.Node))
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownNode, $"There is no node {reference.Node}.") { Wire = wire });
            }

            return null;
        }

        IReadOnlyList<PinDeclaration>? pins = output ? node.Outputs : node.Inputs;
        if (pins is null)
        {
            // The inputs of a signal node that is not found are not known: its own error says why.
            return null;
        }

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
        Dictionary<string, CheckedNode> nodes,
        HashSet<PinReference> wired,
        List<GraphError> errors)
    {
        foreach (NodeDefinition definition in graph.Nodes)
        {
            // An input of a signal node without a wire or a value takes the value of the parameter of the version.
            if (!nodes.TryGetValue(definition.Id, out CheckedNode? node) || !ReferenceEquals(node.Node, definition) || node.Kind.Name == NodeCatalog.Signal)
            {
                continue;
            }

            foreach (PinDeclaration input in node.Inputs!)
            {
                if (!wired.Contains(new PinReference(definition.Id, input.Name)) && !definition.Inputs.ContainsKey(input.Name))
                {
                    errors.Add(
                        new GraphError(
                            GraphErrorCode.MissingInput,
                            input.WireOnly ? "The input needs a wire." : "The input needs a wire or a value.")
                        {
                            Node = definition.Id,
                            Pin = input.Name
                        });
                }
            }
        }
    }

    private static void CheckCycles(GraphDefinition graph, Dictionary<string, CheckedNode> nodes, List<GraphError> errors)
    {
        var next = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (WireDefinition wire in graph.Wires.Where(wire => nodes.ContainsKey(wire.From.Node) && nodes.ContainsKey(wire.To.Node)
                                                                  && !IsFeedback(nodes[wire.To.Node].Kind.Name, wire.To.Pin)))
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
                    var cycle = path.Skip(path.IndexOf(target)).ToList();
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

    // A node that is checked further, with its pins; the inputs are null when nobody knows them.
    private sealed record CheckedNode(
        NodeDefinition Node,
        NodeKind Kind,
        IReadOnlyList<PinDeclaration>? Inputs,
        IReadOnlyList<PinDeclaration> Outputs,
        Signal? Signal);

    // The numbers in invariant writing, whatever the culture of the machine (task 18.4).
    private static string OutOfBounds(decimal value, decimal? minimum, decimal? maximum)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The value {value} is outside [{(minimum is { } low ? low.ToString(CultureInfo.InvariantCulture) : "...")}, {(maximum is { } high ? high.ToString(CultureInfo.InvariantCulture) : "...")}].");
    }
}
