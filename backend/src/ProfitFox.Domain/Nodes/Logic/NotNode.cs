using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Nodes.Logic;

/// <summary>
///     <c>not</c>: the opposite of its input; missing stays missing.
/// </summary>
internal sealed class NotNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromBoolean(!inputs[0].Boolean);
    }
}
