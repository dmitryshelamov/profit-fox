namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Contracts;

internal sealed class LiveOrderDb
{
    public Guid BotId { get; init; }

    public int Number { get; init; }

    public int PositionNumber { get; init; }

    public string? NodePath { get; init; }

    public required string Side { get; init; }

    public required string Type { get; init; }

    public decimal? Price { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Amount { get; init; }

    public decimal Reserve { get; init; }

    public required string Status { get; init; }

    public decimal FilledQuantity { get; init; }

    public string? ExchangeOrderId { get; init; }

    public string? RejectReason { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset? NextAttemptAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}