using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Engine.Strategy.Nodes.Data;

/// <summary>
///     <c>parameters</c>: the values of the graph's parameters.
/// </summary>
internal sealed class ParametersNode(GraphDefinition graph) : Node
{
    private readonly PinValue[] _values = [.. graph.Parameters.Select(parameter => PinValue.From(parameter.Value))];

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        _values.CopyTo(outputs);
    }
}
