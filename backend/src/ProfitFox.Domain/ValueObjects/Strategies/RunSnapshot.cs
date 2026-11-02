namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     What a run needs to go on after a restart: the current position, the active orders, the realized PnL of the positions
///     before it and the last order number. The money of the bot is not kept: it follows from the balance, so a changed balance
///     counts at once.
/// </summary>
/// <param name="Position">The number of the current position.</param>
/// <param name="Quantity"></param>
/// <param name="Invested"></param>
/// <param name="Collateral"></param>
/// <param name="PastPnl">The realized PnL of every closed position of the run.</param>
/// <param name="RealizedPnl"></param>
/// <param name="Fees"></param>
/// <param name="LastEntryPrice"></param>
/// <param name="LastEntryAt"></param>
/// <param name="Closing">The current position closes and waits for its orders.</param>
/// <param name="SkippedOrders"></param>
/// <param name="Orders"></param>
/// <param name="LastOrderNumber"></param>
public sealed record RunSnapshot(
    int Position,
    decimal Quantity,
    decimal Invested,
    decimal Collateral,
    decimal PastPnl,
    decimal RealizedPnl,
    decimal Fees,
    decimal? LastEntryPrice,
    DateTimeOffset? LastEntryAt,
    bool Closing,
    long SkippedOrders,
    IReadOnlyList<RunOrder> Orders,
    int LastOrderNumber);