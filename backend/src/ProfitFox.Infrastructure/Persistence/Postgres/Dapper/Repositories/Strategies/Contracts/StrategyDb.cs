namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

internal sealed class StrategyDb
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The definition as JSON text, the same document the API takes.</summary>
    public required string Definition { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}