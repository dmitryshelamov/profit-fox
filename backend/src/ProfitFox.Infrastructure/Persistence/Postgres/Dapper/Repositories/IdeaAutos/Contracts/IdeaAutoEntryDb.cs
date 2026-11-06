namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

internal sealed class IdeaAutoEntryDb
{
    public Guid Id { get; init; }

    public Guid IdeaId { get; init; }

    public Guid AutoId { get; init; }

    public Guid AccountId { get; init; }

    public decimal Amount { get; init; }

    public decimal MinRiskReward { get; init; }

    public int Rank { get; init; }

    public int Fired { get; init; }

    public DateTimeOffset Minute { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}