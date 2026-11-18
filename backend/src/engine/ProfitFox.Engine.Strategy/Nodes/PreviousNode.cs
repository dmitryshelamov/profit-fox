using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     <c>previous</c>: the value of the input on the previous close of the instrument, that is on the previous candle given for it;
///     absent on the first close and when the input was absent on the previous one.
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