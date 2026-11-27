using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;
using ProfitFox.Domain.ValueObjects.Positions;

namespace ProfitFox.Domain.Services.Nodes.Position;

/// <summary>
///     <c>position</c>: the state of the position of the pair, a source with no input it reads (spec strategy-execution
///     «Узел позиции», «Состояние закрытой позиции»). Its input <c>orders</c> only shows where the orders go; the run of the
///     strategy reads them from the nodes of the orders. The output <c>position</c> carries no value: it shows the link of
///     an exit node to the position.
/// </summary>
internal sealed class PositionStateNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        PositionState state = context.Position;
        bool open = state.Open;
        outputs[0] = PinValue.Missing(PinType.Position);
        outputs[1] = PinValue.FromBoolean(open);
        outputs[2] = PinValue.FromNumber(open ? state.Quantity : 0m);
        outputs[3] = PinValue.FromNumber(open ? state.AveragePrice : null);
        outputs[4] = PinValue.FromNumber(open ? state.LastEntryPrice : null);
        outputs[5] = PinValue.FromNumber(open ? state.Invested : 0m);
        outputs[6] = PinValue.FromDuration(open && state.LastEntryTime is { } last ? context.Moment - last : null);
    }
}
