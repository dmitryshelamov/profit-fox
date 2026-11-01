using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>Money and percentages are decimal strings, counts are numbers; names match the codes results sort by.</summary>
public sealed record MetricsResponse(
    string StartEquity,
    string EndEquity,
    string Pnl,
    string RealizedPnl,
    string UnrealizedPnl,
    string Return,
    string MaxDrawdown,
    string? ReturnToDrawdown,
    int ClosedTrades,
    string? WinRate,
    int Fills,
    string Fees,
    string MaxPositionCost,
    string MaxPositionCostPercent,
    string MaxCapitalUsed,
    string TimeInPosition,
    string TimeUnderWater,
    int LongestUnderWater,
    long SkippedOrders,
    int Candles)
{
    public static MetricsResponse Of(BacktestMetrics metrics)
    {
        return new MetricsResponse(
            BacktestResponse.Text(metrics.StartEquity),
            BacktestResponse.Text(metrics.EndEquity),
            BacktestResponse.Text(metrics.Pnl),
            BacktestResponse.Text(metrics.RealizedPnl),
            BacktestResponse.Text(metrics.UnrealizedPnl),
            BacktestResponse.Text(metrics.ReturnPercent),
            BacktestResponse.Text(metrics.MaxDrawdownPercent),
            metrics.ReturnToDrawdown is { } ratio ? BacktestResponse.Text(ratio) : null,
            metrics.ClosedTrades,
            metrics.WinRatePercent is { } rate ? BacktestResponse.Text(rate) : null,
            metrics.Fills,
            BacktestResponse.Text(metrics.Fees),
            BacktestResponse.Text(metrics.MaxPositionCost),
            BacktestResponse.Text(metrics.MaxPositionCostPercent),
            BacktestResponse.Text(metrics.MaxCapitalUsed),
            BacktestResponse.Text(metrics.TimeInPositionPercent),
            BacktestResponse.Text(metrics.TimeUnderWaterPercent),
            metrics.LongestUnderWaterCandles,
            metrics.SkippedOrders,
            metrics.Candles);
    }
}