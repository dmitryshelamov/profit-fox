using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The last <c>length</c> minutes of a pair, grown a minute at a time, with the sum of their volumes, their low and their high kept as
///     they come (decision 1 of speed-up-ideas-replay): the replay reads the windows of every interval every minute of history, and a pass
///     over up to 1 440 minutes each time was the hot spot of a turn. The numbers are exactly those of a pass over the window: the sum is kept
///     by adding the minute that comes and taking away the one that leaves, then put on the scale of the minutes in the window, since a sum of
///     decimals carries the largest scale of its terms; the low and the high are the first minutes of their value in the window, as
///     <c>Min</c> and <c>Max</c> take them, kept in queues of the candidates in order of value.
/// </summary>
public sealed class MinuteWindow(int length)
{
    private readonly Queue<IdeaCandle> _minutes = new Queue<IdeaCandle>(length);
    private readonly Candidates _lows = new Candidates(length, (candidate, minute) => candidate.Low > minute.Low);
    private readonly Candidates _highs = new Candidates(length, (candidate, minute) => candidate.High < minute.High);

    // How many minutes in the window have each scale of volume, so the sum goes on the largest one among them.
    private readonly int[] _scales = new int[29];
    private decimal _sum;
    private long _added;

    public int Count => _minutes.Count;

    public IdeaCandle First => _minutes.Peek();

    public IdeaCandle Last { get; private set; } = null!;

    public decimal Sum
    {
        get
        {
            int scale = 28;

            while (scale > 0 && _scales[scale] == 0)
            {
                scale--;
            }

            return _sum.Scale > scale ? Math.Round(_sum, scale) : _sum;
        }
    }

    public decimal Low => _lows.Front.Low;

    public decimal High => _highs.Front.High;

    public void Add(IdeaCandle minute)
    {
        if (_minutes.Count == length)
        {
            IdeaCandle left = _minutes.Dequeue();
            _sum -= left.Volume;
            _scales[left.Volume.Scale]--;
        }

        _minutes.Enqueue(minute);
        _sum += minute.Volume;
        _scales[minute.Volume.Scale]++;
        Last = minute;
        _added++;
        _lows.Add(minute, _added, _added - length);
        _highs.Add(minute, _added, _added - length);
    }

    /// <summary>
    ///     The minutes of the window that may still be its low (or high), in order: a minute that comes drops the ones before it that are worse,
    ///     so the first is the best and, among equals, the earliest. A minute drops out of the front when it leaves the window. Kept in a ring
    ///     of the window's length, which the candidates never outgrow.
    /// </summary>
    private sealed class Candidates(int length, Func<IdeaCandle, IdeaCandle, bool> worse)
    {
        private readonly (long Number, IdeaCandle Minute)[] _ring = new (long, IdeaCandle)[length];
        private int _head;
        private int _count;

        public IdeaCandle Front => _ring[_head].Minute;

        public void Add(IdeaCandle minute, long number, long leftUntil)
        {
            while (_count > 0 && _ring[_head].Number <= leftUntil)
            {
                _head = (_head + 1) % length;
                _count--;
            }

            while (_count > 0 && worse(_ring[(_head + _count - 1) % length].Minute, minute))
            {
                _count--;
            }

            _ring[(_head + _count) % length] = (number, minute);
            _count++;
        }
    }
}