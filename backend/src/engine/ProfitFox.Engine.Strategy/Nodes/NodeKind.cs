using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     A kind of node of the library: its pins and settings, and how to create its node for one instrument.
/// </summary>
internal sealed class NodeKind
{
    private readonly Func<IReadOnlyDictionary<string, string>, Node> _create;

    /// <summary>
    ///     The name a definition gives in <c>kind</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     The inputs, in the order <see cref="Node.Compute" /> gets them.
    /// </summary>
    public IReadOnlyList<PinDeclaration> Inputs { get; }

    /// <summary>
    ///     The outputs, in the order <see cref="Node.Compute" /> fills them.
    /// </summary>
    public IReadOnlyList<PinDeclaration> Outputs { get; }

    /// <summary>
    ///     The settings the kind accepts.
    /// </summary>
    public IReadOnlyList<SettingDeclaration> Settings { get; }

    public NodeKind(
        string name,
        IReadOnlyList<PinDeclaration> inputs,
        IReadOnlyList<PinDeclaration> outputs,
        IReadOnlyList<SettingDeclaration> settings,
        Func<IReadOnlyDictionary<string, string>, Node> create)
    {
        Name = name;
        Inputs = inputs;
        Outputs = outputs;
        Settings = settings;
        _create = create;
    }

    /// <summary>
    ///     A new node of this kind for one instrument; <paramref name="settings" /> hold every setting, defaults included.
    /// </summary>
    public Node Create(IReadOnlyDictionary<string, string> settings)
    {
        return _create(settings);
    }
}