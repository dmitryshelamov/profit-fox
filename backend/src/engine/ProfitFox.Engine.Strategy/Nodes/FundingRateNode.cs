using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     <c>funding-rate</c>: the rate of the last funding payment of the instrument at or before the close, a payment at the close
///     included; absent before the first payment.
/// </summary>
internal sealed class FundingRateNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromNumber(context.LastFundingPayment?.Rate);
    }
}