namespace ProfitFox.Domain.Entities.LiveTrading;

/// <summary>
///     A position of a live bot as its books keep it. <see cref="Open" /> holds at least the minimum quantity of the instrument,
///     so a carried remainder alone is no open position; <see cref="ClosedAt" /> is set once it is closed.
/// </summary>
public sealed record LivePosition(
    Guid BotId,
    int Number,
    bool Open,
    bool Closing,
    decimal Quantity,
    decimal Invested,
    decimal Collateral,
    decimal RealizedPnl,
    decimal Fees,
    decimal? LastEntryPrice,
    DateTimeOffset? LastEntryAt,
    long SkippedOrders,
    DateTimeOffset? ClosedAt);