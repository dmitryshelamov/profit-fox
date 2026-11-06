namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

internal sealed class IdeaAutoRefusalDb
{
    public Guid Id { get; init; }

    public Guid IdeaId { get; init; }

    public Guid AutoId { get; init; }

    public required string Mode { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Setup { get; init; }

    public required string Interval { get; init; }

    public required string Type { get; init; }

    public required string Side { get; init; }

    public required string[] Reasons { get; init; }

    public string? Detail { get; init; }
}