using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Nodes.Logic;

/// <summary>
///     <c>outputs</c>: takes the declared outputs of the graph; the run reads them from its inputs.
/// </summary>
internal sealed class OutputsNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
    }
}
