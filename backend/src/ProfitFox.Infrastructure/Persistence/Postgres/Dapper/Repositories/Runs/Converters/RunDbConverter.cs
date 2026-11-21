using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Converters;

internal static class RunDbConverter
{
    internal static Run ToDomain(RunDb row)
    {
        return Run.Restore(
            row.Id,
            row.Hypothesis,
            row.Version,
            row.Period,
            row.Parity,
            row.Status,
            row.QueuedAt,
            row.StartedAt,
            row.FinishedAt,
            row.LeaseOwner,
            row.LeasedUntil,
            row.LostLeases,
            row.CancelRequested,
            row.PairsDone,
            row.PairsTotal);
    }
}
