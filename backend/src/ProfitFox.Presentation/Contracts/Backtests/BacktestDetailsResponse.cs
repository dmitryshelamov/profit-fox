using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>Fills, trades and the equity curve of a result, at most 5000 points, with the metrics they give.</summary>
public sealed record BacktestDetailsResponse(
    MetricsResponse Metrics,
    IReadOnlyList<FillResponse> Fills,
    IReadOnlyList<TradeResponse> Trades,
    IReadOnlyList<EquityPointResponse> Equity)
{
    public static BacktestDetailsResponse Of(BacktestDetails details)
    {
        return new BacktestDetailsResponse(
            MetricsResponse.Of(details.Metrics),
            [
                .. details.Fills.Select(fill => new FillResponse(
                    fill.At,
                    StrategyCodes.ToCode(fill.Side),
                    StrategyCodes.ToCode(fill.Type),
                    BacktestResponse.Text(fill.Price),
                    BacktestResponse.Text(fill.Quantity),
                    BacktestResponse.Text(fill.Fee),
                    fill.FeeAsset == FeeAsset.Quote ? "quote" : "base"))
            ],
            [
                .. details.Trades.Select(trade => new TradeResponse(
                    trade.OpenedAt,
                    trade.ClosedAt,
                    BacktestResponse.Text(trade.EntryPrice),
                    trade.ExitPrice is { } exit ? BacktestResponse.Text(exit) : null,
                    trade.Pnl is { } pnl ? BacktestResponse.Text(pnl) : null,
                    BacktestResponse.Text(trade.Fees)))
            ],
            [.. details.Equity.Select(point => new EquityPointResponse(point.At, BacktestResponse.Text(point.Equity), BacktestResponse.Text(point.MinEquity)))]);
    }
}