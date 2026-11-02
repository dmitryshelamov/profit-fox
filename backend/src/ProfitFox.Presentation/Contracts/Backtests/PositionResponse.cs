namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     A position of a result with its orders; prices are averages of its own fills, without the remainder it carried in, fees are in the
///     quote asset.
/// </summary>
/// <param name="Number"></param>
/// <param name="Status"><c>waiting</c>, <c>open</c>, <c>closing</c> or <c>closed</c> at the end of the period.</param>
/// <param name="OpenedAt">The time of its first fill.</param>
/// <param name="ClosedAt"></param>
/// <param name="CarriedQuantity">The remainder the previous position left below the minimum quantity.</param>
/// <param name="EntryPrice"></param>
/// <param name="ExitPrice"></param>
/// <param name="Pnl">The realized PnL.</param>
/// <param name="Fees"></param>
/// <param name="Orders"></param>
public sealed record PositionResponse(
    int Number,
    string Status,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt,
    string CarriedQuantity,
    string? EntryPrice,
    string? ExitPrice,
    string Pnl,
    string Fees,
    IReadOnlyList<OrderResponse> Orders);