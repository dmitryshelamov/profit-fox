namespace ProfitFox.Domain.Entities.Backtests.Enums;

/// <summary>What results of a backtest can be sorted by: the number of the combination, its calculation time or a metric.</summary>
public enum ResultMetric
{
    Combination,
    CalculationTime,
    StartEquity,
    EndEquity,
    Pnl,
    RealizedPnl,
    UnrealizedPnl,
    Return,
    MaxDrawdown,
    ReturnToDrawdown,
    ClosedTrades,
    WinRate,
    Fills,
    Fees,
    MaxPositionCost,
    MaxPositionCostPercent,
    MaxCapitalUsed,
    TimeInPosition,
    TimeUnderWater,
    LongestUnderWater,
    SkippedOrders,
    Candles
}