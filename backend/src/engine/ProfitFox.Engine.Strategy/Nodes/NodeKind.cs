using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     A kind of node in the catalog: its pins and settings. The pins of <c>parameters</c> and <c>outputs</c> come from the
///     graph they are in.
/// </summary>
public sealed class NodeKind
{
    private readonly Func<GraphDefinition, IReadOnlyList<PinDeclaration>> _inputs;
    private readonly Func<GraphDefinition, IReadOnlyList<PinDeclaration>> _outputs;

    public string Name { get; }

    public IReadOnlyList<SettingDeclaration> Settings { get; }

    internal NodeKind(
        string name,
        Func<GraphDefinition, IReadOnlyList<PinDeclaration>> inputs,
        Func<GraphDefinition, IReadOnlyList<PinDeclaration>> outputs,
        IReadOnlyList<SettingDeclaration> settings)
    {
        Name = name;
        _inputs = inputs;
        _outputs = outputs;
        Settings = settings;
    }

    internal NodeKind(string name, IReadOnlyList<PinDeclaration> inputs, IReadOnlyList<PinDeclaration> outputs, IReadOnlyList<SettingDeclaration> settings)
        : this(name, _ => inputs, _ => outputs, settings)
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
}