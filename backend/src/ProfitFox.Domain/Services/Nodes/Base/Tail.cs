namespace ProfitFox.Domain.Services.Nodes.Base;

/// <summary>
///     The last values a node was given, at most <c>capacity</c> of them, in a ring: <c>this[0]</c> is the latest,
///     <c>this[1]</c> the one before. <see cref="Count" /> counts every value ever added, so it also says how many candles
///     the node has seen.
/// </summary>
internal sealed class Tail<T>(int capacity)
{
    private readonly T[] _items = new T[capacity];

    public long Count { get; private set; }

    /// <summary>
    ///     How many of the last values are kept: the smaller of <see cref="Count" /> and the capacity.
    /// </summary>
    public int Kept => (int)Math.Min(Count, _items.Length);

    /// <summary>
    ///     The value <paramref name="back" /> places before the latest; 0 is the latest.
    /// </summary>
    public T this[int back]
    {
        get
        {
            if (back < 0 || back >= Kept)
            {
                throw new ArgumentOutOfRangeException(nameof(back), back, $"Only {Kept} values are kept.");
            }

            return _items[(int)((Count - 1 - back) % _items.Length)];
        }
    }

    public void Add(T item)
    {
        _items[(int)(Count % _items.Length)] = item;
        Count++;
    }
}
