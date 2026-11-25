using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.ValueObjects.Nodes;

/// <summary>
///     A kind of node in the catalog: its pins and settings. The pins of <c>parameters</c> and <c>outputs</c> come from the
///     graph they are in, the inputs of <c>signal</c> from the version of the signal its node points at. A kind that places
///     orders, holds the position or points at another signal is not allowed in a signal.
/// </summary>
public sealed class NodeKind
{
    private readonly Func<NodeDefinition, GraphDefinition, Node> _create;
    private readonly Func<NodeDefinition?, GraphDefinition, SignalSet, IReadOnlyList<PinDeclaration>> _inputs;
    private readonly Func<NodeDefinition?, GraphDefinition, SignalSet, IReadOnlyList<PinDeclaration>> _outputs;

    public string Name { get; }

    public IReadOnlyList<SettingDeclaration> Settings { get; }

    /// <summary>
    ///     Whether a signal may have the node: a signal reads only the market (spec strategy-graph «Сигнал»).
    /// </summary>
    public bool AllowedInSignal { get; internal init; } = true;

    /// <summary>
    ///     Why a value of a free setting (<see cref="SettingDeclaration.Free" />) is wrong, or null when it is right.
    /// </summary>
    internal Func<string, string, string?> CheckFreeSetting { get; init; } = (_, _) => null;

    /// <summary>
    ///     Why a value written on an input of the node is wrong although of the right type, or null when it is right: the
    ///     input <c>length</c> of a window (change add-setup-strategies, design.md, decision 3).
    /// </summary>
    internal Func<string, GraphValue, string?> CheckValue { get; init; } = (_, _) => null;

    internal NodeKind(
        string name,
        Func<NodeDefinition?, GraphDefinition, SignalSet, IReadOnlyList<PinDeclaration>> inputs,
        Func<NodeDefinition?, GraphDefinition, SignalSet, IReadOnlyList<PinDeclaration>> outputs,
        IReadOnlyList<SettingDeclaration> settings,
        Func<NodeDefinition, GraphDefinition, Node> create)
    {
        Name = name;
        _inputs = inputs;
        _outputs = outputs;
        Settings = settings;
        _create = create;
    }

    internal NodeKind(
        string name,
        Func<GraphDefinition, IReadOnlyList<PinDeclaration>> inputs,
        Func<GraphDefinition, IReadOnlyList<PinDeclaration>> outputs,
        IReadOnlyList<SettingDeclaration> settings,
        Func<NodeDefinition, GraphDefinition, Node> create)
        : this(name, (_, graph, _) => inputs(graph), (_, graph, _) => outputs(graph), settings, create)
    {
    }

    internal NodeKind(
        string name,
        IReadOnlyList<PinDeclaration> inputs,
        IReadOnlyList<PinDeclaration> outputs,
        IReadOnlyList<SettingDeclaration> settings,
        Func<NodeDefinition, Node> create)
        : this(name, _ => inputs, _ => outputs, settings, (node, _) => create(node))
    {
    }

    internal NodeKind(
        string name,
        IReadOnlyList<PinDeclaration> inputs,
        IReadOnlyList<PinDeclaration> outputs,
        IReadOnlyList<SettingDeclaration> settings,
        Func<NodeDefinition, GraphDefinition, Node> create)
        : this(name, _ => inputs, _ => outputs, settings, create)
    {
    }

    /// <summary>
    ///     The inputs of a node of this kind seen apart from any node and any saved signal: what the catalog shows.
    /// </summary>
    public IReadOnlyList<PinDeclaration> Inputs(GraphDefinition graph)
    {
        return Inputs(node: null, graph, SignalSet.Empty);
    }

    public IReadOnlyList<PinDeclaration> Outputs(GraphDefinition graph)
    {
        return Outputs(node: null, graph, SignalSet.Empty);
    }

    /// <summary>
    ///     The inputs of <paramref name="node" /> in <paramref name="graph" />; a kind whose pins depend on neither reads
    ///     only the graph.
    /// </summary>
    public IReadOnlyList<PinDeclaration> Inputs(NodeDefinition? node, GraphDefinition graph, SignalSet signals)
    {
        return _inputs(node, graph, signals);
    }

    public IReadOnlyList<PinDeclaration> Outputs(NodeDefinition? node, GraphDefinition graph, SignalSet signals)
    {
        return _outputs(node, graph, signals);
    }

    /// <summary>
    ///     A new node of this kind with its own state, for one pair.
    /// </summary>
    internal Node Create(NodeDefinition node, GraphDefinition graph)
    {
        return _create(node, graph);
    }
}
