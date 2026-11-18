using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     Executes a composite node on closed candles of all instruments, moment by moment: before <see cref="Close" /> of a moment the
///     caller gives every funding payment at or before it with <see cref="AddFundingPayment" />. Each instrument has its own state of
///     the nodes. The executor has no clock: how far the data goes is the caller's choice.
/// </summary>
public sealed class GraphExecutor
{
    private readonly Step[] _steps;
    private readonly IReadOnlyList<(string Name, Source Source)> _outputs;
    private readonly Dictionary<string, Pair> _pairs = new Dictionary<string, Pair>(StringComparer.Ordinal);
    private DateTimeOffset? _lastClose;

    public GraphExecutor(CompositeNode graph)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        List<Step> steps = [];
        List<(string Name, Source Source)> outputs = [];
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (node.Kind == NodeCatalog.OutputsKind)
            {
                outputs.AddRange(graph.Outputs.Select(output => (output.Name, SourceOf(graph, node, output.Name, indexes))));
                continue;
            }

            NodeKind kind = NodeCatalog.Find(node.Kind)!;
            Source[] sources = kind.Inputs.Select(input => SourceOf(graph, node, input.Name, indexes)).ToArray();
            indexes[node.Id] = steps.Count;
            steps.Add(new Step(node, kind, sources));
        }

        _steps = steps.ToArray();
        _outputs = outputs;
    }

    /// <summary>
    ///     Adds a funding payment. The payments of an instrument come in time order, and a payment at or before a close already computed
    ///     is rejected: the result of that close would have depended on it.
    /// </summary>
    /// <exception cref="ArgumentException">The payment is out of order.</exception>
    public void AddFundingPayment(FundingPayment payment)
    {
        if (payment.Time <= _lastClose)
        {
            throw new ArgumentException($"The payment of {payment.InstrumentId} at {payment.Time:O} is not after the last close {_lastClose:O}.", nameof(payment));
        }

        Pair pair = PairOf(payment.InstrumentId);
        if (payment.Time < pair.LastPaymentTime)
        {
            throw new ArgumentException(
                $"The payment of {payment.InstrumentId} at {payment.Time:O} is before its payment at {pair.LastPaymentTime:O}.",
                nameof(payment));
        }

        pair.Pending.Enqueue(payment);
        pair.LastPaymentTime = payment.Time;
    }

    /// <summary>
    ///     Computes the graph on the close of a moment for each of its candles, in their order. Close times grow strictly from moment to
    ///     moment; every candle closes at <paramref name="closeTime" />, and an instrument appears at most once. Instruments without a
    ///     candle in the moment keep their state.
    /// </summary>
    /// <exception cref="ArgumentException">The moment or its candles are out of order; nothing is computed.</exception>
    public IReadOnlyList<CloseResult> Close(DateTimeOffset closeTime, IReadOnlyList<ClosedCandle> candles)
    {
        CheckMoment(closeTime, candles);
        _lastClose = closeTime;

        var results = new List<CloseResult>(candles.Count);
        foreach (ClosedCandle candle in candles)
        {
            results.Add(Compute(PairOf(candle.InstrumentId), candle));
        }

        return results;
    }

    private static Source SourceOf(CompositeNode graph, NodeDefinition node, string input, Dictionary<string, int> indexes)
    {
        WireDefinition? wire = graph.Wires.FirstOrDefault(wire => wire.To.Node == node.Id && wire.To.Pin == input);
        if (wire is null)
        {
            return new Source(Step: -1, Output: -1, node.Inputs[input]);
        }

        NodeKind from = NodeCatalog.Find(graph.Nodes.First(other => other.Id == wire.From.Node).Kind)!;
        int output = from.Outputs.Select(pin => pin.Name).ToList().IndexOf(wire.From.Pin);
        return new Source(indexes[wire.From.Node], output, Constant: default);
    }

    private void CheckMoment(DateTimeOffset closeTime, IReadOnlyList<ClosedCandle> candles)
    {
        if (closeTime <= _lastClose)
        {
            throw new ArgumentException($"The close {closeTime:O} is not after the last close {_lastClose:O}.", nameof(closeTime));
        }

        var instruments = new HashSet<string>(StringComparer.Ordinal);
        foreach (ClosedCandle candle in candles)
        {
            if (candle.CloseTime != closeTime || candle.OpenTime >= candle.CloseTime)
            {
                throw new ArgumentException(
                    $"The candle of {candle.InstrumentId} {candle.OpenTime:O}–{candle.CloseTime:O} does not close at {closeTime:O}.",
                    nameof(candles));
            }

            if (!instruments.Add(candle.InstrumentId))
            {
                throw new ArgumentException($"The instrument {candle.InstrumentId} has two candles at {closeTime:O}.", nameof(candles));
            }
        }
    }

    private CloseResult Compute(Pair pair, ClosedCandle candle)
    {
        while (pair.Pending.TryPeek(out FundingPayment? payment) && payment.Time <= candle.CloseTime)
        {
            pair.Known = pair.Pending.Dequeue();
        }

        if (pair.Nodes is null)
        {
            pair.Nodes = _steps.Select(step => step.Kind.Create(Settings(step))).ToArray();
            pair.Values = _steps.Select(step => new PinValue[step.Kind.Outputs.Count]).ToArray();
        }

        var context = new NodeContext(candle.InstrumentId, candle.CloseTime, pair.Known);
        for (int index = 0; index < _steps.Length; index++)
        {
            Step step = _steps[index];
            PinValue[] inputs = step.Sources.Select(source => Value(pair, source)).ToArray();
            pair.Nodes[index].Compute(context, inputs, pair.Values![index]);
        }

        var outputs = _outputs.ToDictionary(output => output.Name, output => Value(pair, output.Source), StringComparer.Ordinal);
        return new CloseResult(candle.InstrumentId, candle.CloseTime, outputs);
    }

    private static PinValue Value(Pair pair, Source source)
    {
        return source.Step < 0 ? source.Constant : pair.Values![source.Step][source.Output];
    }

    private static Dictionary<string, string> Settings(Step step)
    {
        var settings = step.Kind.Settings
            .Where(setting => setting.Default is not null)
            .ToDictionary(setting => setting.Name, setting => setting.Default!, StringComparer.Ordinal);
        foreach ((string name, string value) in step.Node.Settings)
        {
            settings[name] = value;
        }

        return settings;
    }

    private Pair PairOf(string instrumentId)
    {
        if (!_pairs.TryGetValue(instrumentId, out Pair? pair))
        {
            pair = new Pair();
            _pairs[instrumentId] = pair;
        }

        return pair;
    }

    // Where an input takes its value: an output of an earlier step, or a constant when Step is -1.
    private readonly record struct Source(int Step, int Output, PinValue Constant);

    private sealed record Step(NodeDefinition Node, NodeKind Kind, Source[] Sources);

    // The state of one instrument: its nodes and their last outputs, created at its first candle, and its funding payments.
    private sealed class Pair
    {
        public Node[]? Nodes { get; set; }

        public PinValue[][]? Values { get; set; }

        public Queue<FundingPayment> Pending { get; } = new Queue<FundingPayment>();

        public FundingPayment? Known { get; set; }

        public DateTimeOffset? LastPaymentTime { get; set; }
    }
}