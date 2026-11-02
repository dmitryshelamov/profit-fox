namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;

internal sealed class ExchangeAccountDb
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string ApiKey { get; init; }

    public long Uid { get; init; }

    public required string[] Categories { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}