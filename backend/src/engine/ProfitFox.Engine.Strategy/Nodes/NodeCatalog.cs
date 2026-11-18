using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Nodes.Data;
using ProfitFox.Engine.Strategy.Nodes.Logic;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     The kinds of nodes a graph may use. A kind that reached the release branch never changes what it computes (spec
///     strategy-execution): new behaviour is a new kind or a new setting whose default keeps the old one.
/// </summary>
public static class NodeCatalog
{
    public const string Parameters = "parameters";
    public const string Candle = "candle";
    public const string FundingRate = "funding-rate";
    public const string Compare = "compare";
    public const string Previous = "previous";
    public const string And = "and";
    public const string Or = "or";
    public const string Not = "not";
    public const string FirstTime = "first-time";
    public const string Outputs = "outputs";

    /// <summary>
    ///     The operations of <c>compare</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> CompareOperations = ["gt", "ge", "lt", "le", "eq", "ne"];

    private static readonly PinDeclaration BooleanA = new PinDeclaration("a", PinType.Boolean);
    private static readonly PinDeclaration BooleanB = new PinDeclaration("b", PinType.Boolean);
    private static readonly PinDeclaration BooleanResult = new PinDeclaration("result", PinType.Boolean);

    private static readonly Dictionary<string, NodeKind> Kinds = new[]
        {
            new NodeKind(
                Parameters,
                _ => [],
                graph => [.. graph.Parameters.Select(parameter => new PinDeclaration(parameter.Name, parameter.Type))],
                [],
                (_, graph) => new ParametersNode(graph)),
            new NodeKind(
                Candle,
                [],
                [.. new[] { "open", "high", "low", "close", "volume" }.Select(name => new PinDeclaration(name, PinType.Number))],
                [],
                _ => new CandleNode()),
            new NodeKind(FundingRate, [], [new PinDeclaration("value", PinType.Number)], [], _ => new FundingRateNode()),
            new NodeKind(
                Compare,
                [new PinDeclaration("a", PinType.Number), new PinDeclaration("b", PinType.Number)],
                [BooleanResult],
                [new SettingDeclaration("operation", CompareOperations, Default: null)],
                node => new CompareNode(node.Settings["operation"])),
            new NodeKind(
                Previous,
                [new PinDeclaration("value", PinType.Number)],
                [new PinDeclaration("result", PinType.Number)],
                [],
                _ => new PreviousNode()),
            new NodeKind(And, [BooleanA, BooleanB], [BooleanResult], [], _ => new AndNode()),
            new NodeKind(Or, [BooleanA, BooleanB], [BooleanResult], [], _ => new OrNode()),
            new NodeKind(Not, [new PinDeclaration("value", PinType.Boolean)], [BooleanResult], [], _ => new NotNode()),
            new NodeKind(
                FirstTime,
                [new PinDeclaration("value", PinType.Boolean)],
                [new PinDeclaration("event", PinType.Event)],
                [],
                _ => new FirstTimeNode()),
            new NodeKind(
                Outputs,
                graph => [.. graph.Outputs.Select(output => new PinDeclaration(output.Name, output.Type))],
                _ => [],
                [],
                (_, _) => new OutputsNode())
        }
        .ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    public static NodeKind? Find(string name)
    {
        return Kinds.GetValueOrDefault(name);
    }
}