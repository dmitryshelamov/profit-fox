using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The types × intervals whose exit rule changed (decision 7): the live ideas put them here when they read a new rule, the lane of the
///     statistics takes them and recomputes their buckets. Thread-safe.
/// </summary>
public sealed class IdeaRecomputes
{
    private readonly Lock _lock = new Lock();
    private readonly HashSet<(IdeaType, Interval)> _changed = [];
    private TaskCompletionSource _added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Add(IdeaType type, Interval interval)
    {
        lock (_lock)
        {
            _changed.Add((type, interval));
        }

        Volatile.Read(ref _added).TrySetResult();
    }

    /// <summary>The types × intervals changed since the last take, each once.</summary>
    public IReadOnlyList<(IdeaType Type, Interval Interval)> Take()
    {
        lock (_lock)
        {
            (IdeaType, Interval)[] taken = [.. _changed];
            _changed.Clear();

            return taken;
        }
    }

    /// <summary>Waits for a type × interval to be added, at most <paramref name="timeout" />; one added before the wait ends it at once.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        TaskCompletionSource added = Volatile.Read(ref _added);
        await Task.WhenAny(added.Task, Task.Delay(timeout, time, cancellationToken));
        Interlocked.CompareExchange(ref _added, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), added);
    }
}