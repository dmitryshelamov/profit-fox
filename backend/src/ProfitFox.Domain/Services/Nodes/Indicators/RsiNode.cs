using FacioQuo.Stock.Indicators;

using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Indicators;

/// <summary>
///     <c>rsi</c>: <c>100 − 100 / (1 + G / L)</c> of <c>value</c> from candle to candle, the gains and the losses smoothed by
///     Wilder, counted by <see cref="RsiList" /> of the package one value at a close (spec strategy-execution «RSI»;
///     change add-setup-strategies, design.md, decision 1). No result until there are <c>length + 1</c> values. With no
///     loss over the window the package gives 100, a still price included; with no gain, 0 (task 2.6). A candle without a
///     value adds nothing and has no result. A computation again after a fill adds nothing and gives what the last close
///     gave. A new length starts the list again on the values kept.
/// </summary>
internal sealed class RsiNode : Node
{
    // The values to start a list again on when the length changes: the length read when it is fixed, 1000 otherwise.
    private readonly Tail<(DateTime Time, double Value)> _values = new Tail<(DateTime Time, double Value)>(WindowLength.Maximum);
    private RsiList? _list;
    private decimal? _output;

    /// <param name="fixedLength">
    ///     Whether the length is the same at every candle (<see cref="WindowLength.Fixed" />): only then does the node keep
    ///     no more than the length read; otherwise it keeps <see cref="WindowLength.Maximum" /> to start a list again on.
    /// </param>
    public RsiNode(bool fixedLength)
    {
        if (!fixedLength)
        {
            _values.Reserve(WindowLength.Maximum);
        }
    }

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            _output = Next(context.Candle.OpenTime.UtcDateTime, inputs[0].Number, WindowLength.Of(inputs[1].Number));
        }

        outputs[0] = PinValue.FromNumber(_output);
    }

    private decimal? Next(DateTime time, decimal? value, int? length)
    {
        if (value is not { } number)
        {
            return null;
        }

        (DateTime Time, double Value) point = (time, (double)number);
        if (length is { } kept)
        {
            _values.Reserve(kept);
        }

        _values.Add(point);
        if (length is { } periods && _list?.LookbackPeriods != periods)
        {
            _list = Start(periods);
        }
        else
        {
            _list?.Add(point.Time, point.Value);
        }

        return length is not null && _list is { Count: > 0 } list ? Indicator.ToDecimal(list[^1].Rsi) : null;
    }

    private RsiList Start(int periods)
    {
        var list = new RsiList(periods) { MaxListSize = Indicator.KeptResults };
        for (int back = _values.Kept - 1; back >= 0; back--)
        {
            list.Add(_values[back].Time, _values[back].Value);
        }

        return list;
    }
}
