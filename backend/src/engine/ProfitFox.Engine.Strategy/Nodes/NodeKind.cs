using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     A kind of node in the catalog: its pins and settings. The pins of <c>parameters</c> and <c>outputs</c> come from the
///     graph they are in. A kind that places orders or holds the position is not allowed in a signal.
/// </summary>
public sealed class NodeKind
{
    private readonly Func<NodeDefinition, GraphDefinition, Node> _create;
    private readonly Func<GraphDefinition, IReadOnlyList<PinDeclaration>> _inputs;
    private readonly Func<GraphDefinition, IReadOnlyList<PinDeclaration>> _outputs;

    public string Name { get; }

    public IReadOnlyList<SettingDeclaration> Settings { get; }

    /// <summary>
    ///     Whether a signal may have the node: a signal reads only the market (spec strategy-graph «Сигнал»).
    /// </summary>
    public bool AllowedInSignal { get; internal init; } = true;

    internal NodeKind(
        string name,
        Func<GraphDefinition, IReadOnlyList<PinDeclaration>> inputs,
        Func<GraphDefinition, IReadOnlyList<PinDeclaration>> outputs,
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
        IReadOnlyList<PinDeclaration> inputs,
        IReadOnlyList<PinDeclaration> outputs,
        IReadOnlyList<SettingDeclaration> settings,
        Func<NodeDefinition, Node> create)
        : this(name, _ => inputs, _ => outputs, settings, (node, _) => create(node))
    {
    }

    public IReadOnlyList<PinDeclaration> Inputs(GraphDefinition graph)
    {
        return _inputs(graph);
    }

    public IReadOnlyList<PinDeclaration> Outputs(GraphDefinition graph)
    {
        return _outputs(graph);
    }

    /// <summary>
    ///     A new node of this kind with its own state, for one pair.
    /// </summary>
    internal Node Create(NodeDefinition node, GraphDefinition graph)
    {
        return _create(node, graph);
    }
}
