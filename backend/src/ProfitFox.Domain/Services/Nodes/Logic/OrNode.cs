using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Logic;

/// <summary>
///     <c>or</c>: true when either input is true, otherwise missing when either is missing.
/// </summary>
internal sealed class OrNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        bool? a = inputs[0].Boolean;
        bool? b = inputs[1].Boolean;
        outputs[0] = PinValue.FromBoolean(a == true || b == true ? true : a is null || b is null ? null : false);
    }
}
