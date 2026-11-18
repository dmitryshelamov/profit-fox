using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     A node of a graph on one instrument: it keeps the state of that instrument and computes its outputs once on each close of the
///     instrument's candle.
/// </summary>
internal abstract class Node
{
    /// <summary>
    ///     Computes the outputs from the inputs, in the order its kind declares them.
    /// </summary>
    public abstract void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs);
}