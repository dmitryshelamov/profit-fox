namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

internal sealed class BacktestChunkDb
{
    public Guid BacktestId { get; init; }

    public int Number { get; init; }

    public long FirstCombination { get; init; }

    public int Combinations { get; init; }

    public int LostLeases { get; init; }
}