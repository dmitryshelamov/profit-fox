using ProfitFox.Domain.Entities.Runs;

namespace ProfitFox.Application.Contracts.Runs.Converters;

public static class RunInternalConverter
{
    public static RunInternal ToInternal(this Run run)
    {
        return new RunInternal(
            run.Id,
            run.Hypothesis,
            run.Version,
            run.Period,
            run.Parity,
            run.Status,
            run.QueuedAt,
            run.StartedAt,
            run.FinishedAt,
            run.LeaseOwner,
            run.LeasedUntil,
            run.LostLeases,
            run.CancelRequested,
            run.PairsDone,
            run.PairsTotal);
    }
}
