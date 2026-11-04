namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

internal sealed class ScannerStateDb
{
    public DateTimeOffset? SnapshotAt { get; init; }

    public int PairsTotal { get; init; }

    public int PairsBackfilled { get; init; }

    public DateTimeOffset? RestPausedUntil { get; init; }
}