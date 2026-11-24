using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Numbers;

/// <summary>
///     <c>select</c>: <c>a</c> when <c>when</c> is true, <c>b</c> when it is false, missing when it is unknown (spec
///     strategy-execution «Выбор значения»). The input not chosen does not count, even when it is missing: the stop of a
///     strategy with two sides takes the distance of the side whose event came (change add-setup-strategies, design.md,
///     decision 5).
/// </summary>
internal sealed class SelectNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromNumber(inputs[0].Boolean switch
        {
            true => inputs[1].Number,
            false => inputs[2].Number,
            null => null
        });
    }
}
