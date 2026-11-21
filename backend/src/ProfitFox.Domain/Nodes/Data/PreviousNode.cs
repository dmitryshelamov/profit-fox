using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Nodes.Data;

/// <summary>
///     <c>previous</c>: the value of its input at the previous closed candle of the pair.
/// </summary>
internal sealed class PreviousNode : Node
{
    private decimal? _previous;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromNumber(_previous);
        _previous = inputs[0].Number;
    }
}
