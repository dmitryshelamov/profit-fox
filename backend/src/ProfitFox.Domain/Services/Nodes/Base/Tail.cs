namespace ProfitFox.Domain.Services.Nodes.Base;

/// <summary>
///     The last values a node was given, in a ring: <c>this[0]</c> is the latest, <c>this[1]</c> the one before.
///     <see cref="Count" /> counts every value ever added, so it also says how many candles the node has seen. The ring holds
///     as many values as <see cref="Reserve" /> asked for, never more than <c>maximum</c> (change add-setup-strategies,
///     design.md, decision 3). It grows when asked and never shrinks; the values added before it grew are not brought
///     back. A ring of the longest window made up front cost every node of every pair its whole size: the graphs of the
///     scanner, a pair each, did not fit 6 GB.
/// </summary>
internal sealed class Tail<T>(int maximum)
{
    private T[] _items = [];
    private int _kept;

    public long Count { get; private set; }

    /// <summary>
    ///     How many of the last values are kept: no more than <see cref="Count" /> and the room <see cref="Reserve" /> made.
    /// </summary>
    public int Kept => _kept;

    /// <summary>
    ///     The value <paramref name="back" /> places before the latest; 0 is the latest.
    /// </summary>
    public T this[int back]
    {
        get
        {
            if (back < 0 || back >= _kept)
            {
                throw new ArgumentOutOfRangeException(nameof(back), back, $"Only {_kept} values are kept.");
            }

            return _items[(int)((Count - 1 - back) % _items.Length)];
        }
    }

    /// <summary>
    ///     Makes room for the last <paramref name="capacity" /> values, up to the maximum; the values kept stay.
    /// </summary>
    public void Reserve(int capacity)
    {
        int wanted = Math.Min(capacity, maximum);
        if (wanted <= _items.Length)
        {
            return;
        }

        var items = new T[wanted];
        for (int back = 0; back < _kept; back++)
        {
            items[(int)((Count - 1 - back) % wanted)] = this[back];
        }

        _items = items;
    }

    public void Add(T item)
    {
        if (_items.Length > 0)
        {
            _items[(int)(Count % _items.Length)] = item;
            _kept = Math.Min(_kept + 1, _items.Length);
        }

        Count++;
    }
}
