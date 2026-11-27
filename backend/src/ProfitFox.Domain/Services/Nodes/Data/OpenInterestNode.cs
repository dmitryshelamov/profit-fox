using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Data;

/// <summary>
///     <c>open-interest</c> (change add-idea-score, spec strategy-execution «Открытый интерес»): the value of the last point of
///     open interest with a time before the close of the candle, as the market-data service gives it; missing before the first
///     one.
/// </summary>
internal sealed class OpenInterestNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromNumber(context.OpenInterest?.Value);
    }
}
