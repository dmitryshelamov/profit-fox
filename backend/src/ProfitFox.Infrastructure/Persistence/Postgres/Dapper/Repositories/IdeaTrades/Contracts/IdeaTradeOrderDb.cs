namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradeOrderDb
{
    public Guid TradeId { get; init; }

    public int Number { get; init; }

    public required string Role { get; init; }

    public required string Side { get; init; }

    public required string Type { get; init; }

    public required string TimeInForce { get; init; }

    public decimal? Price { get; init; }

    public decimal Quantity { get; init; }

    public decimal? TakeProfit { get; init; }

    public decimal? StopLoss { get; init; }

    public bool ReduceOnly { get; init; }

    public required string Status { get; init; }

    public decimal FilledQuantity { get; init; }

    public decimal FilledValue { get; init; }

    public decimal Fee { get; init; }

    public string? ExchangeOrderId { get; init; }

    public string? RejectReason { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset? NextAttemptAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}