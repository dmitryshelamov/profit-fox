using ProfitFox.Domain.Services.Nodes.Base;

namespace ProfitFox.Domain.Services.Nodes.Windows;

/// <summary>
///     The values of an input on the last closed candles of a pair, with what a window of the length read at the candle
///     needs kept up to date (change add-setup-strategies, design.md, decisions 2 and 3): the last values in a ring —
///     <see cref="WindowLength.Maximum" /> and one when the length may change, one more than the length when it may not —
///     and the state of the window for one length: a monotonic queue for the largest and the smallest, a running sum, a
///     sorted list for the median and the percentile. A new length builds the state again from the ring; the length a
///     graph reads stays the same within a run, so that happens once.
/// </summary>
internal sealed class WindowValues
{
    private readonly WindowOrder _order;

    // One more than the longest window: the value that leaves a window of the longest length is still there to leave.
    private readonly Tail<decimal?> _values = new Tail<decimal?>(WindowLength.Maximum + 1);
    private readonly LinkedList<(long Index, decimal Value)> _queue = [];
    private readonly List<decimal> _sorted = [];
    private decimal _sum;
    private bool _sumBroken;
    private int _missing;
    private int _length;

    /// <param name="order">What the state of the window keeps.</param>
    /// <param name="fixedLength">
    ///     Whether the length is the same at every candle (<see cref="WindowLength.Fixed" />): the ring then grows to the
    ///     length read, otherwise it holds the longest window from the start.
    /// </param>
    public WindowValues(WindowOrder order, bool fixedLength)
    {
        _order = order;
        if (!fixedLength)
        {
            _values.Reserve(WindowLength.Maximum + 1);
        }
    }

    /// <summary>
    ///     Adds the value of the closed candle; a missing value counts as a gap in the windows it is in. The length read at the
    ///     candle, when there is one, makes room for its window first; a fixed length is the same at every candle, so the
    ///     ring holds its window from the first candle on.
    /// </summary>
    public void Add(decimal? value, int? length)
    {
        if (length is { } window)
        {
            _values.Reserve(window + 1);
        }

        _values.Add(value);
        if (_length == 0)
        {
            return;
        }

        Enter(_values.Count - 1, value);
        if (_values.Kept > _length)
        {
            Leave(_values.Count - 1 - _length, _values[_length]);
        }
    }

    /// <summary>
    ///     Whether the last <paramref name="length" /> candles are all closed and all have a value; the state is then the
    ///     state of that window.
    /// </summary>
    public bool Full(int length)
    {
        if (length != _length)
        {
            Build(length);
        }

        return _values.Kept >= length && _missing == 0;
    }

    /// <summary>
    ///     The largest or the smallest value of the full window, as <see cref="WindowOrder" /> says.
    /// </summary>
    public decimal Extreme => _queue.First!.Value.Value;

    /// <summary>
    ///     The sum of the full window, or null when it does not fit a decimal.
    /// </summary>
    public decimal? Sum
    {
        get
        {
            if (!_sumBroken)
            {
                return _sum;
            }

            // A running sum that overflowed once is counted again from the window.
            try
            {
                decimal sum = 0;
                for (int back = 0; back < _length; back++)
                {
                    sum += _values[back]!.Value;
                }

                _sum = sum;
                _sumBroken = false;
                return sum;
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }

    /// <summary>
    ///     The values of the full window in ascending order.
    /// </summary>
    public IReadOnlyList<decimal> Sorted => _sorted;

    /// <summary>
    ///     The value <paramref name="back" /> candles before the latest.
    /// </summary>
    public decimal? Back(int back)
    {
        return _values[back];
    }

    private void Build(int length)
    {
        _length = length;
        _queue.Clear();
        _sorted.Clear();
        _sum = 0;
        _sumBroken = false;
        _missing = 0;
        for (int back = Math.Min(length, _values.Kept) - 1; back >= 0; back--)
        {
            Enter(_values.Count - 1 - back, _values[back]);
        }
    }

    private void Enter(long index, decimal? value)
    {
        if (value is not { } number)
        {
            _missing++;
            return;
        }

        switch (_order)
        {
            case WindowOrder.Largest or WindowOrder.Smallest:
                while (_queue.Last is { } last && (_order == WindowOrder.Largest ? last.Value.Value <= number : last.Value.Value >= number))
                {
                    _queue.RemoveLast();
                }

                _queue.AddLast((index, number));
                break;
            case WindowOrder.Sum:
                try
                {
                    _sum += number;
                }
                catch (OverflowException)
                {
                    _sumBroken = true;
                }

                break;
            case WindowOrder.Sorted:
                int place = _sorted.BinarySearch(number);
                _sorted.Insert(place < 0 ? ~place : place, number);
                break;
        }
    }

    private void Leave(long index, decimal? value)
    {
        if (value is not { } number)
        {
            _missing--;
            return;
        }

        switch (_order)
        {
            case WindowOrder.Largest or WindowOrder.Smallest:
                if (_queue.First is { } first && first.Value.Index == index)
                {
                    _queue.RemoveFirst();
                }

                break;
            case WindowOrder.Sum:
                try
                {
                    _sum -= number;
                }
                catch (OverflowException)
                {
                    _sumBroken = true;
                }

                break;
            case WindowOrder.Sorted:
                _sorted.RemoveAt(_sorted.BinarySearch(number));
                break;
        }
    }
}

/// <summary>
///     What the state of a window keeps: the largest or the smallest value, the sum, the values in order, or nothing but
///     the values themselves.
/// </summary>
internal enum WindowOrder
{
    None,
    Largest,
    Smallest,
    Sum,
    Sorted
}
