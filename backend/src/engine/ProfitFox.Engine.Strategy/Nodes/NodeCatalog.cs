using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     The kinds of node of the library. A merged kind never changes what it computes (docs/adr/0005-node-behaviour-is-frozen.md): new
///     behaviour is a new kind or a new value of a setting.
/// </summary>
internal static class NodeCatalog
{
    /// <summary>
    ///     The kind of the node inside a composite node whose inputs are the declared outputs.
    /// </summary>
    public const string OutputsKind = "outputs";

    private static readonly PinDeclaration NumberA = new PinDeclaration("a", PinType.Number);
    private static readonly PinDeclaration NumberB = new PinDeclaration("b", PinType.Number);
    private static readonly PinDeclaration BooleanA = new PinDeclaration("a", PinType.Boolean);
    private static readonly PinDeclaration BooleanB = new PinDeclaration("b", PinType.Boolean);
    private static readonly PinDeclaration BooleanResult = new PinDeclaration("result", PinType.Boolean);

    private static readonly Dictionary<string, NodeKind> Kinds = new NodeKind[]
        {
            new NodeKind(
                "funding-rate",
                inputs: [],
                outputs: [new PinDeclaration("value", PinType.Number)],
                settings: [],
                _ => new FundingRateNode()),
            new NodeKind(
                "previous",
                inputs: [new PinDeclaration("value", PinType.Number)],
                outputs: [new PinDeclaration("result", PinType.Number)],
                settings: [],
                _ => new PreviousNode()),
            new NodeKind(
                "compare",
                inputs: [NumberA, NumberB],
                outputs: [BooleanResult],
                settings: [new SettingDeclaration("operation", [CompareNode.GreaterOrEqual, CompareNode.Less], Default: null)],
                settings => new CompareNode(settings["operation"])),
            new NodeKind(
                "and",
                inputs: [BooleanA, BooleanB],
                outputs: [BooleanResult],
                settings: [],
                _ => new AndNode())
        }
        .ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    /// <summary>
    ///     The kind with the name, or null when the library has none; <see cref="OutputsKind" /> is not among them.
    /// </summary>
    public static NodeKind? Find(string name)
    {
        return Kinds.GetValueOrDefault(name);
    }
}