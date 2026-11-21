using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph.Model;

namespace ProfitFox.Domain.Nodes.Data;

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
