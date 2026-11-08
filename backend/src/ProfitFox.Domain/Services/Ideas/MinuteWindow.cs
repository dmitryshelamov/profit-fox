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
    private readonly Candidates _lows = new Candidates((candidate, minute) => candidate.Low > minute.Low);
    private readonly Candidates _highs = new Candidates((candidate, minute) => candidate.High < minute.High);

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
    ///     so the first is the best and, among equals, the earliest. A minute drops out of the front when it leaves the window.
    /// </summary>
    private sealed class Candidates(Func<IdeaCandle, IdeaCandle, bool> worse)
    {
        private readonly List<(long Number, IdeaCandle Minute)> _queue = [];
        private int _front;

        public IdeaCandle Front => _queue[_front].Minute;

        public void Add(IdeaCandle minute, long number, long leftUntil)
        {
            while (_front < _queue.Count && _queue[_front].Number <= leftUntil)
            {
                _front++;
            }

            while (_queue.Count > _front && worse(_queue[^1].Minute, minute))
            {
                _queue.RemoveAt(_queue.Count - 1);
            }

            if (_front > 0 && _front == _queue.Count)
            {
                _queue.Clear();
                _front = 0;
            }

            _queue.Add((number, minute));
        }
    }
}