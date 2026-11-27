using FacioQuo.Stock.Indicators;

using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Indicators;

/// <summary>
///     <c>atr</c>: the true range of the candles of the pair smoothed by Wilder, counted by <see cref="AtrList" /> of the
///     package one candle at a close (spec strategy-execution «Средний истинный диапазон»; change add-setup-strategies,
///     design.md, decision 1). No result until the pair has <c>length + 1</c> candles. The value depends on the candle the
///     count started at and converges («Сходимость ATR и RSI»). A computation again after a fill adds nothing and gives
///     what the last close gave. A new length starts the list again on the candles kept. The length is from 2 to 1000: the
///     package does not count an ATR of one candle, so a written 1 is an error of the graph and a 1 from a wire gives no
///     value (task 8.2; design.md, decision 3).
/// </summary>
internal sealed class AtrNode : Node
{
    /// <summary>
    ///     The shortest length <see cref="AtrList" /> takes: it refuses 1.
    /// </summary>
    public const int MinimumLength = 2;

    // The candles to start a list again on when the length changes: the length read when it is fixed, 1000 otherwise.
    private readonly Tail<Bar> _candles = new Tail<Bar>(WindowLength.Maximum);
    private AtrList? _list;
    private decimal? _output;

    /// <param name="fixedLength">
    ///     Whether the length is the same at every candle (<see cref="WindowLength.Fixed" />): only then does the node keep
    ///     no more than the length read; otherwise it keeps <see cref="WindowLength.Maximum" /> to start a list again on.
    /// </param>
    public AtrNode(bool fixedLength)
    {
        if (!fixedLength)
        {
            _candles.Reserve(WindowLength.Maximum);
        }
    }

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            ClosedCandle candle = context.Candle;
            var bar = new Bar(candle.OpenTime.UtcDateTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
            // The package counts an ATR from a length of 2 (task 2.6); a length of 1 gives no value.
            int? length = WindowLength.Of(inputs[0].Number, MinimumLength);
            if (length is { } kept)
            {
                _candles.Reserve(kept);
            }

            _candles.Add(bar);
            if (length is { } periods && _list?.LookbackPeriods != periods)
            {
                _list = Start(periods);
            }
            else
            {
                _list?.Add(bar);
            }

            _output = length is not null && _list is { Count: > 0 } list ? Indicator.ToDecimal(list[^1].Atr) : null;
        }

        outputs[0] = PinValue.FromNumber(_output);
    }

    private AtrList Start(int periods)
    {
        var list = new AtrList(periods) { MaxListSize = Indicator.KeptResults };
        for (int back = _candles.Kept - 1; back >= 0; back--)
        {
            list.Add(_candles[back]);
        }

        return list;
    }
}
