namespace ProfitFox.Bench.Application.Preview;

/// <summary>
///     The history of the last few pairs and timeframes the preview read, in memory only (spec signal-preview «Повторный
///     предпросмотр без чтения истории»). History of exploration does not change, so an entry never goes stale; the
///     least recently used one goes when a new one comes. Requests for the same key share one read; a failed read is
///     forgotten, so the next request reads again.
/// </summary>
public sealed class PreviewHistoryCache
{
    public const int DefaultCapacity = 5;

    private readonly int _capacity;
    private readonly Dictionary<(string Instrument, TimeSpan Timeframe), LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = [];
    private readonly Lock _lock = new Lock();

    public PreviewHistoryCache()
        : this(DefaultCapacity)
    {
    }

    public PreviewHistoryCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>
    ///     The history of <paramref name="instrument" /> on <paramref name="timeframe" />: from memory, or from
    ///     <paramref name="read" />, which runs to its end even when the caller that started it gives up.
    /// </summary>
    public Task<PreviewHistory> Get(string instrument, TimeSpan timeframe, Func<Task<PreviewHistory>> read, CancellationToken cancellation)
    {
        var key = (instrument, timeframe);
        Task<PreviewHistory> history;
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<Entry>? found) && !found.Value.History.IsFaulted && !found.Value.History.IsCanceled)
            {
                _order.Remove(found);
                _order.AddFirst(found);
                history = found.Value.History;
            }
            else
            {
                if (found is not null)
                {
                    _order.Remove(found);
                }

                history = Task.Run(read, CancellationToken.None);
                LinkedListNode<Entry> added = _order.AddFirst(new Entry(key, history));
                _entries[key] = added;
                while (_order.Count > _capacity)
                {
                    _entries.Remove(_order.Last!.Value.Key);
                    _order.RemoveLast();
                }

                _ = history.ContinueWith(_ => Forget(added), CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);
            }
        }

        return history.WaitAsync(cancellation);
    }

    private void Forget(LinkedListNode<Entry> entry)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(entry.Value.Key, out LinkedListNode<Entry>? current) && current == entry)
            {
                _entries.Remove(entry.Value.Key);
                _order.Remove(entry);
            }
        }
    }

    private sealed record Entry((string Instrument, TimeSpan Timeframe) Key, Task<PreviewHistory> History);
}
