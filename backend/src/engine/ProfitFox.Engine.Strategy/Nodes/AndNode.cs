using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     <c>and</c>: true when both inputs are true.
/// </summary>
internal sealed class AndNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromBoolean(inputs[0].Boolean && inputs[1].Boolean);
    }
}