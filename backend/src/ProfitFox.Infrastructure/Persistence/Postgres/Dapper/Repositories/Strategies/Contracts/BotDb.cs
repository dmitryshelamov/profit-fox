namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

internal sealed class BotDb
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public Guid StrategyId { get; init; }

    /// <summary>The values the owner gave, as a JSON object.</summary>
    public required string ParameterValues { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Interval { get; init; }

    public decimal Balance { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}