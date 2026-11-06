namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradeFillDb
{
    public required string ExecId { get; init; }

    public Guid TradeId { get; init; }

    public required string ExchangeOrderId { get; init; }

    public decimal Quantity { get; init; }

    public decimal Price { get; init; }

    public decimal Fee { get; init; }

    public required string FeeAsset { get; init; }

    public DateTimeOffset ExecutedAt { get; init; }
}