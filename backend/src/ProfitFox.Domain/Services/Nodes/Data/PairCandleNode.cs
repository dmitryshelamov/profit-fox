using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Data;

/// <summary>
///     <c>pair-candle</c> (change add-ideas-and-market, spec strategy-execution «Свеча другой пары»): open, high, low, close
///     and volume of the closed candle of the pair <paramref name="symbol" /> with the open time of the candle being
///     computed; missing when that pair has no such candle.
/// </summary>
internal sealed class PairCandleNode(string symbol) : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Pairs is not { } pairs || !pairs.TryGetValue(symbol, out ClosedCandle? candle) || candle.OpenTime != context.Candle.OpenTime)
        {
            for (int index = 0; index < outputs.Length; index++)
            {
                outputs[index] = PinValue.Missing(PinType.Number);
            }

            return;
        }

        outputs[0] = PinValue.FromNumber(candle.Open);
        outputs[1] = PinValue.FromNumber(candle.High);
        outputs[2] = PinValue.FromNumber(candle.Low);
        outputs[3] = PinValue.FromNumber(candle.Close);
        outputs[4] = PinValue.FromNumber(candle.Volume);
    }
}
