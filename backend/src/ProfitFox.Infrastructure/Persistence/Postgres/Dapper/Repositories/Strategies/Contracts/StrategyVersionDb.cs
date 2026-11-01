namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

internal sealed class StrategyVersionDb
{
    public Guid StrategyId { get; init; }

    public int Number { get; init; }

    /// <summary>The snapshot as JSON text: the strategy and the composite nodes it uses.</summary>
    public required string Snapshot { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}