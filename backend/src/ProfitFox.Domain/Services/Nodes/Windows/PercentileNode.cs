using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Windows;

/// <summary>
///     <c>percentile</c>: the value of nearest rank among <c>value</c> on the last <c>length</c> closed candles, the current
///     one counted — in ascending order, the place <c>⌈length × rank⌉</c> counted from one (spec strategy-execution
///     «Перцентиль»). <c>rank</c> is above 0 and at most 1. The window fills as that of <c>window</c> does, with a sorted
///     list (change add-setup-strategies, design.md, decision 2).
/// </summary>
internal sealed class PercentileNode : Node
{
    private readonly WindowValues _values = new WindowValues(WindowOrder.Sorted);
    private decimal? _output;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            _values.Add(inputs[0].Number);
            _output = WindowLength.Of(inputs[1].Number) is { } length && _values.Full(length) ? Result(length, inputs[2].Number) : null;
        }

        outputs[0] = PinValue.FromNumber(_output);
    }

    private decimal? Result(int length, decimal? rank)
    {
        if (rank is not ({ } share and > 0 and <= 1))
        {
            return null;
        }

        int place = (int)decimal.Ceiling(length * share);
        return _values.Sorted[Math.Max(place, 1) - 1];
    }
}
