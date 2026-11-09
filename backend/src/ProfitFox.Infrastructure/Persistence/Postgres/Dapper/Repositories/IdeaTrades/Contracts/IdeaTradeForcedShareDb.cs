namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradeForcedShareDb
{
    public required string ExecId { get; init; }

    public Guid TradeId { get; init; }

    public required string Kind { get; init; }

    public decimal Quantity { get; init; }

    public decimal Value { get; init; }

    public decimal Fee { get; init; }

    public DateTimeOffset ExecutedAt { get; init; }
}