using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Nodes.Data;
using ProfitFox.Domain.Nodes.Logic;
using ProfitFox.Domain.Nodes.Position;

namespace ProfitFox.Domain.Nodes;

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
    public const string Enter = "enter";
    public const string StopLoss = "stop-loss";
    public const string TakeProfit = "take-profit";
    public const string CloseAfter = "close-after";

    /// <summary>
    ///     The setting of <c>enter</c> that holds its side, and the sides.
    /// </summary>
    public const string Side = "side";

    public const string Long = "long";
    public const string Short = "short";

    /// <summary>
    ///     The inputs of the position nodes: the event of <c>enter</c>, the distance of <c>stop-loss</c> and
    ///     <c>take-profit</c> as a fraction of the entry price, the time of <c>close-after</c> from the moment of the order.
    /// </summary>
    public const string When = "when";

    public const string Distance = "distance";
    public const string After = "after";

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
                (_, _) => new OutputsNode()),
            new NodeKind(
                Enter,
                [new PinDeclaration(When, PinType.Event)],
                [],
                [new SettingDeclaration(Side, [Long, Short], Default: null)],
                _ => new PositionNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(StopLoss, [new PinDeclaration(Distance, PinType.Number)], [], [], _ => new PositionNode()) { AllowedInSignal = false },
            new NodeKind(TakeProfit, [new PinDeclaration(Distance, PinType.Number)], [], [], _ => new PositionNode()) { AllowedInSignal = false },
            new NodeKind(CloseAfter, [new PinDeclaration(After, PinType.Duration)], [], [], _ => new PositionNode()) { AllowedInSignal = false }
        }
        .ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    /// <summary>
    ///     Every kind, in the order the catalog declares them: the kinds of a signal first, then the position nodes of a
    ///     strategy. The constructor of signals offers those with <see cref="NodeKind.AllowedInSignal" />.
    /// </summary>
    public static IReadOnlyList<NodeKind> All { get; } = [.. Kinds.Values];

    public static NodeKind? Find(string name)
    {
        return Kinds.GetValueOrDefault(name);
    }
}