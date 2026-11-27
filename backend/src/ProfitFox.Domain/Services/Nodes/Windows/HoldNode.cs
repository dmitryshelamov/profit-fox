using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Windows;

/// <summary>
///     <c>hold</c>: <c>value</c> at the last candle where <c>when</c> was true, while no more than <c>length</c> candles
///     closed after it (spec strategy-execution «Запомненное значение»). A missing <c>when</c> is false; a new candle with
///     a true <c>when</c> replaces what is held, a missing value too. The memory moves at the close of a candle only.
/// </summary>
internal sealed class HoldNode : Node
{
    private decimal? _held;
    private long _since = -1;
    private decimal? _output;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            if (inputs[0].Boolean == true)
            {
                _held = inputs[1].Number;
                _since = 0;
            }
            else if (_since >= 0)
            {
                _since++;
            }

            _output = _since >= 0 && WindowLength.Of(inputs[2].Number) is { } length && _since <= length ? _held : null;
        }

        outputs[0] = PinValue.FromNumber(_output);
    }
}
