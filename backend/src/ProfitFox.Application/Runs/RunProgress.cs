namespace ProfitFox.Application.Runs;

/// <summary>
///     How far a run has come: the pairs it has passed of the pairs it will pass. The run writes it, the lease keeper reads it
///     from another thread when it renews the lease.
/// </summary>
public sealed class RunProgress
{
    private int _done;
    private int _total;

    public int Done => Volatile.Read(ref _done);

    public int Total => Volatile.Read(ref _total);

    /// <summary>
    ///     The run knows its pairs and starts from the first.
    /// </summary>
    public void Begin(int total)
    {
        Volatile.Write(ref _done, 0);
        Volatile.Write(ref _total, total);
    }

    /// <summary>
    ///     One more pair is passed.
    /// </summary>
    public void Advance()
    {
        Interlocked.Increment(ref _done);
    }
}
