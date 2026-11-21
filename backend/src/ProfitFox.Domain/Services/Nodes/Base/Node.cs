using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Base;

/// <summary>
///     A node of one pair: computed once at each closed candle, with its inputs and outputs in the order its kind declares
///     them. A node keeps its own state between candles.
/// </summary>
internal abstract class Node
{
    public abstract void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs);
}
