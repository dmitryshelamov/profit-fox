namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Contracts;

internal sealed class LivePositionDb
{
    public Guid BotId { get; init; }

    public int Number { get; init; }

    public bool Open { get; init; }

    public bool Closing { get; init; }

    public decimal Quantity { get; init; }

    public decimal Invested { get; init; }

    public decimal Collateral { get; init; }

    public decimal RealizedPnl { get; init; }

    public decimal Fees { get; init; }

    public decimal? LastEntryPrice { get; init; }

    public DateTimeOffset? LastEntryAt { get; init; }

    public long SkippedOrders { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }
}