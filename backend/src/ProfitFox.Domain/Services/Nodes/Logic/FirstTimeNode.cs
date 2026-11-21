using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Logic;

/// <summary>
///     <c>first-time</c>: an event at the candle where the condition is true and was known false at the previous candle of
///     the pair. No event at the first candle or after a candle where the condition was missing.
/// </summary>
internal sealed class FirstTimeNode : Node
{
    private bool? _previous;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        bool? current = inputs[0].Boolean;
        outputs[0] = PinValue.FromEvent(current == true && _previous == false);
        _previous = current;
    }
}
