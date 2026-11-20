namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     Where a run of the queue is: waiting, going, or ended with a line of the ledger.
/// </summary>
public static class RunState
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Finished = "finished";
}
