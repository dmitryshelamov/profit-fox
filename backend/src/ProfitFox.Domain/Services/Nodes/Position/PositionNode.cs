using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Position;

/// <summary>
///     <c>enter</c>, <c>stop-loss</c>, <c>take-profit</c> and <c>close-after</c>: a node of the position computes nothing;
///     the run of the strategy reads its input at each candle.
/// </summary>
internal sealed class PositionNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
    }
}
