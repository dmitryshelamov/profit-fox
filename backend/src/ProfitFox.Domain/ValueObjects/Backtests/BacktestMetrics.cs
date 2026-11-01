namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What one combination of a backtest gave. Money is in the quote asset, percentages are of the balance unless said otherwise;
///     equity is the cash plus the position at the close of the last processed candle, without the fee of a hypothetical exit.
/// </summary>
/// <param name="StartEquity"></param>
/// <param name="EndEquity"></param>
/// <param name="Pnl">End equity less the balance.</param>
/// <param name="RealizedPnl"></param>
/// <param name="UnrealizedPnl">Of the position open at the end.</param>
/// <param name="ReturnPercent"></param>
/// <param name="MaxDrawdownPercent">The largest fall of equity at closes from its previous peak, in percent of that peak.</param>
/// <param name="ReturnToDrawdown">Missing when there was no drawdown.</param>
/// <param name="ClosedTrades">Position cycles from zero back to zero.</param>
/// <param name="WinRatePercent">Of the closed trades; missing without one.</param>
/// <param name="Fills"></param>
/// <param name="Fees">A fee in the coin counts at the price of its fill.</param>
/// <param name="MaxPositionCost">The largest invested amount.</param>
/// <param name="MaxPositionCostPercent"></param>
/// <param name="MaxCapitalUsed">The largest invested amount plus the reserve of standing limit entries.</param>
/// <param name="TimeInPositionPercent">Of the closes.</param>
/// <param name="TimeUnderWaterPercent">Of the closes: in a position that is worth less than invested.</param>
/// <param name="LongestUnderWaterCandles"></param>
/// <param name="SkippedOrders">Orders not placed for lack of free money or below the exchange minimum.</param>
/// <param name="Candles">Processed candles.</param>
public sealed record BacktestMetrics(
    decimal StartEquity,
    decimal EndEquity,
    decimal Pnl,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal ReturnPercent,
    decimal MaxDrawdownPercent,
    decimal? ReturnToDrawdown,
    int ClosedTrades,
    decimal? WinRatePercent,
    int Fills,
    decimal Fees,
    decimal MaxPositionCost,
    decimal MaxPositionCostPercent,
    decimal MaxCapitalUsed,
    decimal TimeInPositionPercent,
    decimal TimeUnderWaterPercent,
    int LongestUnderWaterCandles,
    long SkippedOrders,
    int Candles);