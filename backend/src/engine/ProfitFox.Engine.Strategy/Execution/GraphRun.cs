using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;
using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Engine.Strategy.Execution;

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
    private DateTimeOffset? _lastClose;

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
    ///     <paramref name="lastFunding" /> is the last payment paid by that close, or null before the first one. The returned
    ///     list is reused by the next call.
    /// </summary>
    public IReadOnlyList<PinValue> Step(ClosedCandle candle, FundingPayment? lastFunding)
    {
        if (candle.CloseTime <= _lastClose)
        {
            throw new ArgumentException($"Candles go in the order of their close: {candle.CloseTime:O} is not after {_lastClose:O}.", nameof(candle));
        }

        if (lastFunding is not null && lastFunding.Time > candle.CloseTime)
        {
            throw new ArgumentException($"A payment at {lastFunding.Time:O} is not known at the close {candle.CloseTime:O}.", nameof(lastFunding));
        }

        _lastClose = candle.CloseTime;
        var context = new NodeContext(candle, lastFunding);
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

    private PinValue Read(InputSource source)
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
