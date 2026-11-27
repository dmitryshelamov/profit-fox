using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Indicators;

/// <summary>
///     <c>zones</c>: the edges of the nearest zones of support and resistance about <c>price</c>, the zones built by the
///     rules of <c>ZoneFinder</c> of v2 on the <c>length</c> closed candles before the current one (spec strategy-execution
///     «Зоны поддержки и сопротивления», «Края ближайших зон»; change add-setup-strategies, design.md, decision 4).
/// </summary>
/// <remarks>
///     A touch is the high of a candle above the highs of the two candles before it and not below the highs of the two
///     after it, or the low by the mirror rule; the five candles are all in the window. A touch is known two candles after
///     its own, so the node keeps a queue of touches with the number of their candle and drops those that left the window.
///     The zones are built again at every candle — <c>atr</c> changes at every candle — from the touches sorted by price,
///     from the bottom up: a touch no further than <c>merge × atr</c> from the lowest touch of a zone goes into it,
///     otherwise it starts a new one. A zone with fewer than <c>strength</c> touches does not count. The current candle is
///     not in the window, so a candle that breaks a zone sees the zone as it was before it.
/// </remarks>
internal sealed class ZonesNode(bool fixedLength) : Node
{
    // The candles a touch is found on: the latest five. The window counts the candles by their number alone.
    private readonly Tail<(decimal High, decimal Low)> _candles = NewCandles();

    // The touches of the last candles kept, oldest first: the candle of the touch and its price.
    private readonly Queue<(long Candle, decimal Price)> _touches = new Queue<(long Candle, decimal Price)>();

    // The touches inside the window of the length read last, oldest first, and their prices in ascending order.
    private readonly Queue<(long Candle, decimal Price)> _window = new Queue<(long Candle, decimal Price)>();
    private readonly List<decimal> _sorted = [];
    private int _length;

    // The longest length the touches are kept for (design.md, decision 3): the longest read so far when the length is
    // fixed (WindowLength.Fixed), the longest there is when it may change.
    private int _longest = fixedLength ? 0 : WindowLength.Maximum;
    private decimal? _above;
    private decimal? _below;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            (_above, _below) = Edges(inputs[0].Number, inputs[1].Number, WindowLength.Of(inputs[2].Number), inputs[3].Number, inputs[4].Number);
            Add(context.Candle);
        }

        outputs[0] = PinValue.FromNumber(_above);
        outputs[1] = PinValue.FromNumber(_below);
    }

    // The edges at the current candle, before it is added: the window is the candles 0..Count-1 already kept.
    private (decimal? Above, decimal? Below) Edges(decimal? atr, decimal? price, int? length, decimal? merge, decimal? strength)
    {
        if (length is not { } window)
        {
            return (null, null);
        }

        _longest = Math.Max(_longest, window);
        Window(window);
        if (_candles.Count < window || atr is not { } range || price is not { } level || merge is not { } share || strength is not { } least)
        {
            return (null, null);
        }

        decimal reach;
        try
        {
            reach = share * range;
        }
        catch (OverflowException)
        {
            return (null, null);
        }

        decimal? above = null;
        decimal? below = null;
        int index = 0;
        while (index < _sorted.Count)
        {
            decimal bottom = _sorted[index];
            decimal top = bottom;
            int count = 1;
            index++;
            while (index < _sorted.Count && Within(_sorted[index], bottom, reach))
            {
                top = _sorted[index];
                count++;
                index++;
            }

            if (count < least)
            {
                continue;
            }

            if (top >= level && (above is null || top < above))
            {
                above = top;
            }

            if (bottom <= level && (below is null || bottom > below))
            {
                below = bottom;
            }
        }

        return (above, below);
    }

    private static bool Within(decimal touch, decimal bottom, decimal reach)
    {
        try
        {
            return touch - bottom <= reach;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    // Keeps in the sorted list the touches of the window of the length: the candles Count-length..Count-1, a touch with
    // both its neighbours on each side among them, so its candle is at least Count-length+2. A new length sorts the kept
    // touches again.
    private void Window(int length)
    {
        long first = _candles.Count - length + 2;
        if (length != _length)
        {
            _length = length;
            _sorted.Clear();
            _window.Clear();
            foreach ((long Candle, decimal Price) touch in _touches.Where(touch => touch.Candle >= first))
            {
                _window.Enqueue(touch);
                Insert(touch.Price);
            }
        }

        while (_window.Count > 0 && _window.Peek().Candle < first)
        {
            Remove(_window.Dequeue().Price);
        }

    }

    private void Add(ClosedCandle candle)
    {
        _candles.Add((candle.High, candle.Low));

        // A touch is kept while a window of the longest length read at a later candle may hold it.
        while (_touches.Count > 0 && _touches.Peek().Candle < _candles.Count - _longest + 2)
        {
            _touches.Dequeue();
        }

        if (_candles.Kept < 5)
        {
            return;
        }

        // The candle two before the latest has its two neighbours on each side now.
        long middle = _candles.Count - 3;
        (decimal high, decimal low) = _candles[2];
        bool top = high > _candles[3].High && high > _candles[4].High && high >= _candles[1].High && high >= _candles[0].High;
        bool bottom = low < _candles[3].Low && low < _candles[4].Low && low <= _candles[1].Low && low <= _candles[0].Low;
        if (top)
        {
            Found((middle, high));
        }

        if (bottom)
        {
            Found((middle, low));
        }
    }

    // A touch found goes into the window of the last length too; the next count drops it when the window is shorter.
    private void Found((long Candle, decimal Price) touch)
    {
        _touches.Enqueue(touch);
        if (_length > 0)
        {
            _window.Enqueue(touch);
            Insert(touch.Price);
        }
    }

    private static Tail<(decimal High, decimal Low)> NewCandles()
    {
        var candles = new Tail<(decimal High, decimal Low)>(5);
        candles.Reserve(5);
        return candles;
    }

    private void Insert(decimal price)
    {
        int place = _sorted.BinarySearch(price);
        _sorted.Insert(place < 0 ? ~place : place, price);
    }

    private void Remove(decimal price)
    {
        int place = _sorted.BinarySearch(price);
        if (place >= 0)
        {
            _sorted.RemoveAt(place);
        }
    }
}
