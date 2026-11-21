using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Runs;

/// <summary>
///     A run of the queue as its row would say: a verdict run of H0001 version 1 on exploration that waits, unless the test
///     gives what it checks.
/// </summary>
public static class RunGenerator
{
    private static readonly DateTimeOffset QueuedAt = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    public static Run Generate(
        string id = "run-1",
        string hypothesis = "H0001",
        int version = 1,
        string period = "exploration",
        bool parity = false,
        string status = RunState.Queued,
        int lostLeases = 0,
        bool cancelRequested = false)
    {
        bool taken = status != RunState.Queued;
        return Run.Restore(
            id,
            hypothesis,
            version,
            period,
            parity,
            status,
            QueuedAt,
            taken ? QueuedAt.AddMinutes(1) : null,
            status == RunState.Finished ? QueuedAt.AddMinutes(2) : null,
            taken ? "worker-1" : null,
            status == RunState.Running ? QueuedAt.AddMinutes(1).AddSeconds(30) : null,
            lostLeases,
            cancelRequested,
            pairsDone: 0,
            pairsTotal: null);
    }
}
