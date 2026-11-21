using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Nodes.Data;

/// <summary>
///     <c>funding-rate</c>: the rate of the last payment paid by the close of the candle, missing before the first one.
/// </summary>
internal sealed class FundingRateNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromNumber(context.LastFunding?.Rate);
    }
}
