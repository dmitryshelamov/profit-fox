using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Data;

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
