using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Data;

/// <summary>
///     <c>previous</c>: the value of its input at the previous closed candle of the pair. A computation again after a fill
///     gives what the last close gave and keeps the memory (change add-position-node, design.md, decision 7).
/// </summary>
internal sealed class PreviousNode : Node
{
    private decimal? _previous;
    private decimal? _output;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            _output = _previous;
            _previous = inputs[0].Number;
        }

        outputs[0] = PinValue.FromNumber(_output);
    }
}
