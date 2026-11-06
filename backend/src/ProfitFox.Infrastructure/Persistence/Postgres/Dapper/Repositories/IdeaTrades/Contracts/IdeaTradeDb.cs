namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradeDb
{
    public Guid Id { get; init; }

    public Guid IdeaId { get; init; }

    public Guid AccountId { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Setup { get; init; }

    public required string Interval { get; init; }

    public required string Type { get; init; }

    public required string Side { get; init; }

    public decimal EntryPrice { get; init; }

    public decimal Stop { get; init; }

    public decimal Target { get; init; }

    public decimal Quantity { get; init; }

    public decimal Amount { get; init; }

    public decimal Risk { get; init; }

    public DateTimeOffset? EntryDeadline { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset StatusAt { get; init; }

    public string? Reason { get; init; }

    public bool CancelRequested { get; init; }

    public bool CloseRequested { get; init; }

    public decimal EntryQuantity { get; init; }

    public decimal EntryValue { get; init; }

    public decimal EntryFee { get; init; }

    public decimal TakeProfitQuantity { get; init; }

    public decimal TakeProfitValue { get; init; }

    public decimal TakeProfitFee { get; init; }

    public decimal StopLossQuantity { get; init; }

    public decimal StopLossValue { get; init; }

    public decimal StopLossFee { get; init; }

    public decimal ManualQuantity { get; init; }

    public decimal ManualValue { get; init; }

    public decimal ManualFee { get; init; }

    public int LastOrderNumber { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public long LeaseEpoch { get; init; }

    /// <summary>Read only by the queries that ask for it.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}