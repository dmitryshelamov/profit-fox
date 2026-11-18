using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Engine.Strategy.Nodes.Logic;

/// <summary>
///     <c>and</c>: false when either input is false, otherwise missing when either is missing.
/// </summary>
internal sealed class AndNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        bool? a = inputs[0].Boolean;
        bool? b = inputs[1].Boolean;
        outputs[0] = PinValue.FromBoolean(a == false || b == false ? false : a is null || b is null ? null : true);
    }
}
