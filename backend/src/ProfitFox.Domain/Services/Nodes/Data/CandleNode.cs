using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Data;

/// <summary>
///     <c>candle</c>: open, high, low, close and volume of the closed candle, and the volume the buyers started (change
///     add-idea-score, design.md, decision 7) — missing when it is not known; the outputs before it keep their places.
/// </summary>
internal sealed class CandleNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        ClosedCandle candle = context.Candle;
        outputs[0] = PinValue.FromNumber(candle.Open);
        outputs[1] = PinValue.FromNumber(candle.High);
        outputs[2] = PinValue.FromNumber(candle.Low);
        outputs[3] = PinValue.FromNumber(candle.Close);
        outputs[4] = PinValue.FromNumber(candle.Volume);
        outputs[5] = PinValue.FromNumber(candle.BuyVolume);
    }
}
