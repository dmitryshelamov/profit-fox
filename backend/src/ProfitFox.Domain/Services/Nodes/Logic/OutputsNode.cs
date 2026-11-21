using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Logic;

/// <summary>
///     <c>outputs</c>: takes the declared outputs of the graph; the run reads them from its inputs.
/// </summary>
internal sealed class OutputsNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
    }
}
