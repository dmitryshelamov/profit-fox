namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;

internal sealed record ExchangeAccountDb
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Exchange { get; init; }

    public string? ApiKey { get; init; }

    public long? Uid { get; init; }

    public string? AccountId { get; init; }

    public required string[] Categories { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}