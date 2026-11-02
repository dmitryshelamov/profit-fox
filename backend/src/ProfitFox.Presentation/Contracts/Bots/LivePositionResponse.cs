using ProfitFox.Application.Backtests;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.LiveTrading.Queries.GetBotPositions;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Presentation.Contracts.Backtests;

namespace ProfitFox.Presentation.Contracts.Bots;

/// <summary>A position of a live bot with its orders; prices are averages of its own fills, fees are in the quote asset.</summary>
/// <param name="Number"></param>
/// <param name="Status"><c>waiting</c>, <c>open</c>, <c>closing</c> or <c>closed</c>.</param>
/// <param name="OpenedAt">The time of its first fill.</param>
/// <param name="ClosedAt"></param>
/// <param name="Quantity"></param>
/// <param name="Invested"></param>
/// <param name="EntryPrice"></param>
/// <param name="ExitPrice"></param>
/// <param name="Pnl">The realized PnL.</param>
/// <param name="Fees"></param>
/// <param name="Orders"></param>
public sealed record LivePositionResponse(
    int Number,
    string Status,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt,
    string Quantity,
    string Invested,
    string? EntryPrice,
    string? ExitPrice,
    string Pnl,
    string Fees,
    IReadOnlyList<LiveOrderResponse> Orders)
{
    public static LivePositionResponse Of(LivePositionDetails details)
    {
        return new LivePositionResponse(
            details.Position.Number,
            details.Status.ToCode(),
            details.OpenedAt,
            details.Position.ClosedAt,
            BacktestResponse.Text(details.Position.Quantity),
            BacktestResponse.Text(details.Position.Invested),
            Text(details.EntryPrice),
            Text(details.ExitPrice),
            BacktestResponse.Text(details.Position.RealizedPnl),
            BacktestResponse.Text(details.Position.Fees),
            [.. details.Orders.Select(LiveOrderResponse.Of)]);
    }

    internal static string? Text(decimal? value)
    {
        return value is { } number ? BacktestResponse.Text(number) : null;
    }
}

/// <summary>An order of a live position with its fills.</summary>
/// <param name="Number">The number of the order in the bot.</param>
/// <param name="Node">The path of the node that placed it; <c>null</c> when the closing position placed it.</param>
/// <param name="Side"></param>
/// <param name="Type"></param>
/// <param name="Price">The limit price; <c>null</c> for a market order.</param>
/// <param name="Quantity"></param>
/// <param name="Amount">The amount of a market order sized by amount.</param>
/// <param name="Status"><c>pending_submit</c>, <c>active</c>, <c>pending_cancel</c>, <c>cancel_sent</c>, <c>filled</c>, <c>cancelled</c> or <c>rejected</c>.</param>
/// <param name="RejectReason">Why the exchange refused it.</param>
/// <param name="Fills"></param>
public sealed record LiveOrderResponse(
    int Number,
    string? Node,
    string Side,
    string Type,
    string? Price,
    string? Quantity,
    string? Amount,
    string Status,
    string? RejectReason,
    IReadOnlyList<FillResponse> Fills)
{
    public static LiveOrderResponse Of(LiveOrderDetails details)
    {
        return new LiveOrderResponse(
            details.Order.Number,
            details.Order.NodePath,
            StrategyCodes.ToCode(details.Order.Side),
            StrategyCodes.ToCode(details.Order.Type),
            LivePositionResponse.Text(details.Order.Price),
            LivePositionResponse.Text(details.Order.Quantity),
            LivePositionResponse.Text(details.Order.Amount),
            details.Order.Status.ToCode(),
            details.Order.RejectReason,
            [
                .. details.Fills.Select(fill => new FillResponse(
                    fill.ExecutedAt,
                    BacktestResponse.Text(fill.Price),
                    BacktestResponse.Text(fill.Quantity),
                    BacktestResponse.Text(fill.Fee),
                    fill.FeeAsset == FeeAsset.Quote ? "quote" : "base"))
            ]);
    }
}