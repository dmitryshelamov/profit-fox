using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Nodes;
using ProfitFox.Domain.ValueObjects.Positions;

namespace ProfitFox.Domain.Services.Execution;

/// <summary>
///     A graph running on the closed candles of one pair (spec strategy-execution): computed once per candle, in the order
///     of close times, with the state of its nodes kept between candles. A new pair is a new run.
/// </summary>
public sealed class GraphRun
{
    private readonly CompiledGraph _graph;
    private readonly Node[] _nodes;
    private readonly PinValue[] _slots;
    private readonly PinValue[] _inputs;
    private readonly PinValue[] _nodeOutputs;
    private readonly PinValue[] _outputs;
    private ClosedCandle? _lastCandle;
    private CandleFacts _lastFacts;
    private IReadOnlyDictionary<string, ClosedCandle>? _lastPairs;

    public GraphRun(GraphDefinition graph)
        : this(Compile(graph))
    {
    }

    internal GraphRun(CompiledGraph graph)
    {
        _graph = graph;
        _nodes = [.. graph.Nodes.Select(node => node.Kind.Create(node.Definition, graph.Graph))];
        _slots = new PinValue[graph.SlotCount];
        _inputs = new PinValue[graph.Nodes.Count == 0 ? 0 : graph.Nodes.Max(node => node.Inputs.Length)];
        _nodeOutputs = new PinValue[graph.Nodes.Count == 0 ? 0 : graph.Nodes.Max(node => node.OutputSlots.Length)];
        _outputs = new PinValue[graph.Outputs.Count];
    }

    /// <summary>
    ///     Computes the graph at the close of <paramref name="candle" /> and gives its declared outputs in their order.
    ///     <paramref name="facts" /> are the last payment paid by that close and the last point of open interest before it,
    ///     each null before the first one;
    ///     <paramref name="position" /> is the state of the position at the close, closed when not given;
    ///     <paramref name="pairs" /> are the closed candles of the pairs the graph names (<c>pair-candle</c>), by symbol,
    ///     none when not given. The memory of the nodes moves to this candle. The returned list is reused by the next call.
    /// </summary>
    public IReadOnlyList<PinValue> Step(
        ClosedCandle candle,
        CandleFacts facts,
        PositionState? position = null,
        IReadOnlyDictionary<string, ClosedCandle>? pairs = null)
    {
        if (candle.CloseTime <= _lastCandle?.CloseTime)
        {
            throw new ArgumentException(
                $"Candles go in the order of their close: {candle.CloseTime:O} is not after {_lastCandle.CloseTime:O}.",
                nameof(candle));
        }

        if (facts.Funding is { } funding && funding.Time > candle.CloseTime)
        {
            throw new ArgumentException($"A payment at {funding.Time:O} is not known at the close {candle.CloseTime:O}.", nameof(facts));
        }

        if (facts.OpenInterest is { } point && point.Time >= candle.CloseTime)
        {
            throw new ArgumentException($"A point of open interest at {point.Time:O} is not known at the close {candle.CloseTime:O}.", nameof(facts));
        }

        _lastCandle = candle;
        _lastFacts = facts;
        _lastPairs = pairs;
        return Compute(new NodeContext(candle, facts.Funding, candle.CloseTime, position ?? PositionState.Closed, Commit: true, pairs, facts.OpenInterest));
    }

    /// <summary>
    ///     Computes the graph again after a fill at <paramref name="moment" /> (spec strategy-execution «Пересчёт графа после
    ///     исполнения»): on the market data of the last closed candle, with the new state of the position, without moving the
    ///     memory of the nodes — «предыдущее» and «впервые» read what they kept at the last close.
    /// </summary>
    public IReadOnlyList<PinValue> Again(PositionState position, DateTimeOffset moment)
    {
        if (_lastCandle is not { } candle)
        {
            throw new InvalidOperationException("The graph is computed again only after a closed candle.");
        }

        if (moment < candle.CloseTime)
        {
            throw new ArgumentException($"A fill at {moment:O} comes after the close {candle.CloseTime:O}.", nameof(moment));
        }

        return Compute(new NodeContext(candle, _lastFacts.Funding, moment, position, Commit: false, _lastPairs, _lastFacts.OpenInterest));
    }

    private IReadOnlyList<PinValue> Compute(NodeContext context)
    {
        for (int index = 0; index < _nodes.Length; index++)
        {
            CompiledNode node = _graph.Nodes[index];
            Span<PinValue> inputs = _inputs.AsSpan(0, node.Inputs.Length);
            for (int input = 0; input < node.Inputs.Length; input++)
            {
                inputs[input] = Read(node.Inputs[input]);
            }

            Span<PinValue> outputs = _nodeOutputs.AsSpan(0, node.OutputSlots.Length);
            _nodes[index].Compute(context, inputs, outputs);
            for (int output = 0; output < node.OutputSlots.Length; output++)
            {
                _slots[node.OutputSlots[output]] = outputs[output];
            }
        }

        for (int output = 0; output < _outputs.Length; output++)
        {
            _outputs[output] = Read(_graph.Outputs[output]);
        }

        return _outputs;
    }

    /// <summary>
    ///     The value an input has now: its written value, or what its wire carried at the last candle; before the first
    ///     candle a wire carries nothing, which reads as missing.
    /// </summary>
    internal PinValue Read(InputSource source)
    {
        return source.Slot >= 0 ? _slots[source.Slot] : source.Value;
    }

    private static CompiledGraph Compile(GraphDefinition graph)
    {
        List<GraphError> errors = GraphValidator.Validate(graph);
        if (errors.Count > 0)
        {
            throw new ArgumentException($"The graph does not pass the checks: {string.Join("; ", errors)}", nameof(graph));
        }

        return CompiledGraph.Compile(graph);
    }
}
