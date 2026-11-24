using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Data;

/// <summary>
///     <c>candles</c>: <c>count</c> candles of the timeframe the graph runs on as a duration (spec strategy-execution «Срок
///     в свечах»). The length of a candle is that of the closed candle itself, <c>CloseTime − OpenTime</c> (change
///     add-setup-strategies, design.md, decision 7), so a strategy on <c>5m</c> and on <c>4h</c> lives the same number of
///     candles. A count that is not a whole number of at least 1, and a duration past the range of a time span, give no
///     value.
/// </summary>
internal sealed class CandlesNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromDuration(Duration(inputs[0].Number, context.Candle.CloseTime - context.Candle.OpenTime));
    }

    private static TimeSpan? Duration(decimal? count, TimeSpan candle)
    {
        if (count is not { } number || number < 1 || number != decimal.Truncate(number) || number > int.MaxValue)
        {
            return null;
        }

        try
        {
            return TimeSpan.FromTicks(checked(candle.Ticks * (long)number));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
