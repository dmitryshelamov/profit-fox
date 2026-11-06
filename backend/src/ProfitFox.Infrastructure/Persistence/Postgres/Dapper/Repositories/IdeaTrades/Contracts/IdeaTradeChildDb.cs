namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradeChildDb
{
    public Guid TradeId { get; init; }

    public required string ExchangeOrderId { get; init; }

    public required string Leg { get; init; }

    public decimal FilledQuantity { get; init; }

    public decimal FilledValue { get; init; }

    public decimal Fee { get; init; }

    public bool Finished { get; init; }
}