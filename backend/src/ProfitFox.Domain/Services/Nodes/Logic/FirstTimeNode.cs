using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Logic;

/// <summary>
///     <c>first-time</c>: an event at the candle where the condition is true and was known false at the previous candle of
///     the pair. No event at the first candle or after a candle where the condition was missing. A computation again after
///     a fill compares its condition with the one of the close before the last and moves nothing (change
///     add-position-node, design.md, decision 7).
/// </summary>
internal sealed class FirstTimeNode : Node
{
    private bool? _previous;
    private bool? _beforePrevious;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        bool? current = inputs[0].Boolean;
        if (!context.Commit)
        {
            outputs[0] = PinValue.FromEvent(current == true && _beforePrevious == false);
            return;
        }

        outputs[0] = PinValue.FromEvent(current == true && _previous == false);
        _beforePrevious = _previous;
        _previous = current;
    }
}
