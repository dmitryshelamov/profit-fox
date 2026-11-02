using ProfitFox.Domain.Services.Strategies.Execution;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A position of a result's details with the orders that have a fill or are still active at the end. Its prices are averages of its own
///     fills weighted by quantity, without the remainder it carried in; <see cref="Fees" /> are in the quote asset.
/// </summary>
/// <param name="Number"></param>
/// <param name="Status">At the end of the period.</param>
/// <param name="OpenedAt">The time of its first fill.</param>
/// <param name="ClosedAt"></param>
/// <param name="CarriedQuantity">The remainder the previous position left below the minimum quantity.</param>
/// <param name="EntryPrice"></param>
/// <param name="ExitPrice"></param>
/// <param name="RealizedPnl"></param>
/// <param name="Fees"></param>
/// <param name="Orders"></param>
public sealed record BacktestPosition(
    int Number,
    PositionStatus Status,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal CarriedQuantity,
    decimal? EntryPrice,
    decimal? ExitPrice,
    decimal RealizedPnl,
    decimal Fees,
    IReadOnlyList<BacktestOrder> Orders);