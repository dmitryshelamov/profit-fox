using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Logic;

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
