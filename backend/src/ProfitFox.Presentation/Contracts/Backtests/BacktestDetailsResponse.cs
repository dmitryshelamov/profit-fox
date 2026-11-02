using ProfitFox.Application.Backtests;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>The positions with their orders and fills and the equity curve of a result, at most 5000 points, with the metrics they give.</summary>
public sealed record BacktestDetailsResponse(
    MetricsResponse Metrics,
    IReadOnlyList<PositionResponse> Positions,
    IReadOnlyList<EquityPointResponse> Equity)
{
    public static BacktestDetailsResponse Of(BacktestDetails details)
    {
        return new BacktestDetailsResponse(
            MetricsResponse.Of(details.Metrics),
            [.. details.Positions.Select(Position)],
            [
                .. details.Equity.Select(point => new EquityPointResponse(
                    point.At,
                    BacktestResponse.Text(point.Equity),
                    BacktestResponse.Text(point.MinEquity),
                    point.OpenTime,
                    BacktestResponse.Text(point.Open),
                    BacktestResponse.Text(point.High),
                    BacktestResponse.Text(point.Low),
                    BacktestResponse.Text(point.Close)))
            ]);
    }

    private static PositionResponse Position(BacktestPosition position)
    {
        return new PositionResponse(
            position.Number,
            position.Status.ToCode(),
            position.OpenedAt,
            position.ClosedAt,
            BacktestResponse.Text(position.CarriedQuantity),
            Text(position.EntryPrice),
            Text(position.ExitPrice),
            BacktestResponse.Text(position.RealizedPnl),
            BacktestResponse.Text(position.Fees),
            [.. position.Orders.Select(Order)]);
    }

    private static OrderResponse Order(BacktestOrder order)
    {
        return new OrderResponse(
            order.Id,
            order.Node,
            StrategyCodes.ToCode(order.Side),
            StrategyCodes.ToCode(order.Type),
            Text(order.Price),
            Text(order.Quantity),
            Text(order.Amount),
            order.Status.ToCode(),
            [
                .. order.Fills.Select(fill => new FillResponse(
                    fill.At,
                    BacktestResponse.Text(fill.Price),
                    BacktestResponse.Text(fill.Quantity),
                    BacktestResponse.Text(fill.Fee),
                    fill.FeeAsset == FeeAsset.Quote ? "quote" : "base"))
            ]);
    }

    private static string? Text(decimal? value)
    {
        return value is { } number ? BacktestResponse.Text(number) : null;
    }
}