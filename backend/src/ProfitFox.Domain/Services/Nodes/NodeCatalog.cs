using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.Services.Nodes.Data;
using ProfitFox.Domain.Services.Nodes.Indicators;
using ProfitFox.Domain.Services.Nodes.Logic;
using ProfitFox.Domain.Services.Nodes.Numbers;
using ProfitFox.Domain.Services.Nodes.Position;
using ProfitFox.Domain.Services.Nodes.Windows;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes;

/// <summary>
///     The kinds of nodes a graph may use. A kind that reached the release branch never changes what it computes (spec
///     strategy-execution): new behaviour is a new kind or a new setting whose default keeps the old one.
/// </summary>
public static class NodeCatalog
{
    public const string Parameters = "parameters";
    public const string Candle = "candle";
    public const string FundingRate = "funding-rate";
    public const string PairCandle = "pair-candle";

    /// <summary>
    ///     The setting of <c>pair-candle</c> that names the pair (change add-ideas-and-market).
    /// </summary>
    public const string Symbol = "symbol";
    public const string Compare = "compare";
    public const string Previous = "previous";
    public const string And = "and";
    public const string Or = "or";
    public const string Not = "not";
    public const string FirstTime = "first-time";
    public const string Add = "add";
    public const string Subtract = "subtract";
    public const string Multiply = "multiply";

    /// <summary>
    ///     The kinds of change add-setup-strategies: numbers, windows over the last candles, indicators and a duration in
    ///     candles.
    /// </summary>
    public const string Divide = "divide";

    public const string Min = "min";
    public const string Max = "max";
    public const string Select = "select";
    public const string Window = "window";
    public const string Percentile = "percentile";
    public const string Hold = "hold";
    public const string Atr = "atr";
    public const string Rsi = "rsi";
    public const string Zones = "zones";
    public const string Candles = "candles";
    public const string Outputs = "outputs";
    public const string Position = "position";
    public const string Enter = "enter";
    public const string MarketOrder = "market-order";
    public const string LimitOrder = "limit-order";
    public const string ClosePosition = "close-position";
    public const string StopLoss = "stop-loss";
    public const string TakeProfit = "take-profit";
    public const string CloseAfter = "close-after";
    public const string Signal = "signal";

    /// <summary>
    ///     The setting of <c>enter</c> that holds its side, and the sides.
    /// </summary>
    public const string Side = "side";

    public const string Long = "long";
    public const string Short = "short";

    /// <summary>
    ///     The settings of <c>market-order</c> and <c>limit-order</c>: the side of the order and whether its size is an
    ///     amount in USDT or a quantity of coins (change add-position-node, design.md, decision 3).
    /// </summary>
    public const string Buy = "buy";

    public const string Sell = "sell";
    public const string SizeIn = "sizeIn";
    public const string Amount = "amount";
    public const string Quantity = "quantity";

    /// <summary>
    ///     The pins of orders and of the position: the output <c>order</c> of a node of an order, the input <c>orders</c> of
    ///     <c>position</c> that takes several wires, its output and the input <c>position</c> of an exit node, and the
    ///     outputs of the state of the position.
    /// </summary>
    public const string Order = "order";

    public const string Orders = "orders";
    public const string PositionPin = "position";
    public const string Open = "open";
    public const string AveragePrice = "averagePrice";
    public const string LastEntryPrice = "lastEntryPrice";
    public const string Invested = "invested";
    public const string SinceLastEntry = "sinceLastEntry";
    public const string Size = "size";
    public const string Price = "price";

    /// <summary>
    ///     The inputs of the position nodes: the event of <c>enter</c>, the distance of <c>stop-loss</c> and
    ///     <c>take-profit</c> as a fraction of the entry price, the time of <c>close-after</c> from the moment of the order.
    /// </summary>
    public const string When = "when";

    public const string Distance = "distance";
    public const string After = "after";

    /// <summary>
    ///     The settings of <c>signal</c> — the id of a saved signal and the number of its version — and its only output
    ///     (spec strategy-graph «Узел сигнала»).
    /// </summary>
    public const string SignalSetting = "signal";

    public const string VersionSetting = "version";
    public const string Event = "event";

    /// <summary>
    ///     The output of a <c>signal</c> node whose version is a signal-state (change add-ideas-and-market).
    /// </summary>
    public const string Value = "value";

    /// <summary>
    ///     The operations of <c>compare</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> CompareOperations = ["gt", "ge", "lt", "le", "eq", "ne"];

    private static readonly PinDeclaration BooleanA = new PinDeclaration("a", PinType.Boolean);
    private static readonly PinDeclaration BooleanB = new PinDeclaration("b", PinType.Boolean);
    private static readonly PinDeclaration BooleanResult = new PinDeclaration("result", PinType.Boolean);
    private static readonly PinDeclaration NumberA = new PinDeclaration("a", PinType.Number);
    private static readonly PinDeclaration NumberB = new PinDeclaration("b", PinType.Number);
    private static readonly PinDeclaration NumberResult = new PinDeclaration("result", PinType.Number);
    private static readonly PinDeclaration OrderOutput = new PinDeclaration(Order, PinType.Order);
    private static readonly PinDeclaration PositionInput = new PinDeclaration(PositionPin, PinType.Position);
    private static readonly PinDeclaration SizeInput = new PinDeclaration(Size, PinType.Number);
    private static readonly PinDeclaration Condition = new PinDeclaration(When, PinType.Boolean);
    private static readonly PinDeclaration NumberValue = new PinDeclaration(Value, PinType.Number);
    private static readonly PinDeclaration Length = new PinDeclaration(WindowLength.Pin, PinType.Number);

    private static readonly SettingDeclaration[] OrderSettings =
    [
        new SettingDeclaration(Side, [Buy, Sell], Default: null),
        new SettingDeclaration(SizeIn, [Amount, Quantity], Default: null)
    ];

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
                PairCandle,
                [],
                [.. new[] { "open", "high", "low", "close", "volume" }.Select(name => new PinDeclaration(name, PinType.Number))],
                [new SettingDeclaration(Symbol, [], Default: null) { Free = true }],
                node => new PairCandleNode(node.Settings[Symbol]))
            {
                CheckFreeSetting = CheckSymbol
            },
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
            new NodeKind(Add, [NumberA, NumberB], [NumberResult], [], _ => new ArithmeticNode((a, b) => a + b)),
            new NodeKind(Subtract, [NumberA, NumberB], [NumberResult], [], _ => new ArithmeticNode((a, b) => a - b)),
            new NodeKind(Multiply, [NumberA, NumberB], [NumberResult], [], _ => new ArithmeticNode((a, b) => a * b)),
            new NodeKind(Divide, [NumberA, NumberB], [NumberResult], [], _ => new ArithmeticNode((a, b) => a / b)),
            new NodeKind(Min, [NumberA, NumberB], [NumberResult], [], _ => new ArithmeticNode(Math.Min)),
            new NodeKind(Max, [NumberA, NumberB], [NumberResult], [], _ => new ArithmeticNode(Math.Max)),
            new NodeKind(Select, [Condition, NumberA, NumberB], [NumberResult], [], _ => new SelectNode()),
            new NodeKind(
                Window,
                [NumberValue, Length],
                [NumberResult],
                [new SettingDeclaration(WindowNode.Operation, WindowNode.Operations, Default: null)],
                node => new WindowNode(node.Settings[WindowNode.Operation]))
            {
                CheckValue = WindowLength.Check
            },
            new NodeKind(Percentile, [NumberValue, Length, new PinDeclaration("rank", PinType.Number)], [NumberResult], [], _ => new PercentileNode())
            {
                CheckValue = WindowLength.Check
            },
            new NodeKind(Hold, [Condition, NumberValue, Length], [NumberResult], [], _ => new HoldNode()) { CheckValue = WindowLength.Check },
            new NodeKind(Atr, [Length], [NumberResult], [], _ => new AtrNode()) { CheckValue = WindowLength.Check },
            new NodeKind(Rsi, [NumberValue, Length], [NumberResult], [], _ => new RsiNode()) { CheckValue = WindowLength.Check },
            new NodeKind(
                Zones,
                [.. new[] { "atr", Price, WindowLength.Pin, "merge", "strength" }.Select(name => new PinDeclaration(name, PinType.Number))],
                [new PinDeclaration("above", PinType.Number), new PinDeclaration("below", PinType.Number)],
                [],
                _ => new ZonesNode())
            {
                CheckValue = WindowLength.Check
            },
            new NodeKind(Candles, [new PinDeclaration("count", PinType.Number)], [new PinDeclaration("duration", PinType.Duration)], [], _ => new CandlesNode()),
            new NodeKind(
                Outputs,
                graph => [.. graph.Outputs.Select(output => new PinDeclaration(output.Name, output.Type))],
                _ => [],
                [],
                (_, _) => new OutputsNode()),
            new NodeKind(
                Position,
                [new PinDeclaration(Orders, PinType.Order) { Many = true }],
                [
                    new PinDeclaration(PositionPin, PinType.Position),
                    new PinDeclaration(Open, PinType.Boolean),
                    new PinDeclaration(Quantity, PinType.Number),
                    new PinDeclaration(AveragePrice, PinType.Number),
                    new PinDeclaration(LastEntryPrice, PinType.Number),
                    new PinDeclaration(Invested, PinType.Number),
                    new PinDeclaration(SinceLastEntry, PinType.Duration)
                ],
                [],
                _ => new PositionStateNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(
                Enter,
                [new PinDeclaration(When, PinType.Event), SizeInput],
                [OrderOutput],
                [new SettingDeclaration(Side, [Long, Short], Default: null)],
                _ => new PositionNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(MarketOrder, [Condition, SizeInput], [OrderOutput], OrderSettings, _ => new PositionNode()) { AllowedInSignal = false },
            new NodeKind(
                LimitOrder,
                [Condition, new PinDeclaration(Price, PinType.Number), SizeInput],
                [OrderOutput],
                OrderSettings,
                _ => new PositionNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(ClosePosition, [Condition], [OrderOutput], [], _ => new PositionNode()) { AllowedInSignal = false },
            new NodeKind(StopLoss, [PositionInput, new PinDeclaration(Distance, PinType.Number)], [OrderOutput], [], _ => new PositionNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(TakeProfit, [PositionInput, new PinDeclaration(Distance, PinType.Number)], [OrderOutput], [], _ => new PositionNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(CloseAfter, [PositionInput, new PinDeclaration(After, PinType.Duration)], [OrderOutput], [], _ => new PositionNode())
            {
                AllowedInSignal = false
            },
            new NodeKind(
                Signal,
                (node, _, signals) => SignalInputs(node, signals),
                (node, _, signals) => SignalOutputs(node, signals),
                [
                    new SettingDeclaration(SignalSetting, [], Default: null) { Free = true },
                    new SettingDeclaration(VersionSetting, [], Default: null) { Free = true }
                ],
                (node, _) => throw new InvalidOperationException($"The signal node {node.Id} is expanded before the graph runs."))
            {
                AllowedInSignal = false,
                CheckFreeSetting = SignalNodes.CheckSetting
            }
        }
        .ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    /// <summary>
    ///     Every kind, in the order the catalog declares them: the kinds of a signal first, then the node <c>position</c>
    ///     and the nodes of orders of a strategy, and its <c>signal</c> node. The constructor of signals offers those with
    ///     <see cref="NodeKind.AllowedInSignal" />.
    /// </summary>
    public static IReadOnlyList<NodeKind> All { get; } = [.. Kinds.Values];

    public static NodeKind? Find(string name)
    {
        return Kinds.GetValueOrDefault(name);
    }

    // A symbol of a pair: capital letters and digits, as the exchange writes it (BTCUSDT, 1000PEPEUSDT). Whether the pair
    // trades is checked where the catalog of pairs is known.
    private static string? CheckSymbol(string name, string value)
    {
        return value.Length > 0 && value.All(letter => letter is >= 'A' and <= 'Z' or >= '0' and <= '9')
            ? null
            : $"{name} names a pair in capital letters and digits, as BTCUSDT, not «{value}».";
    }

    // The output of the node: value of the boolean type for a version whose output is a boolean (change
    // add-ideas-and-market, design.md, decision 7), event for any other, and for a node whose version is not found.
    private static IReadOnlyList<PinDeclaration> SignalOutputs(NodeDefinition? node, SignalSet signals)
    {
        if (node is not null && SignalNodes.TryReference(node, out SignalReference reference) && signals.Find(reference).Signal is { IsState: true })
        {
            return [new PinDeclaration(Value, PinType.Boolean)];
        }

        return [new PinDeclaration(Event, PinType.Event)];
    }

    // The parameters of the version the node points at; none when there is no node, no reference or no such version.
    private static IReadOnlyList<PinDeclaration> SignalInputs(NodeDefinition? node, SignalSet signals)
    {
        if (node is null || !SignalNodes.TryReference(node, out SignalReference reference) || signals.Find(reference).Signal is not { } signal)
        {
            return [];
        }

        return [.. signal.Graph.Parameters.Select(parameter => new PinDeclaration(parameter.Name, parameter.Type))];
    }
}
