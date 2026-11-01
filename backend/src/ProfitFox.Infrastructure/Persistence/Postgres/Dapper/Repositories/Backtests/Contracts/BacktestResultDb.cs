namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

internal sealed class BacktestResultDb
{
    public Guid BacktestId { get; init; }

    public long Combination { get; init; }

    public required string ParameterValues { get; init; }

    public decimal StartEquity { get; init; }

    public decimal EndEquity { get; init; }

    public decimal Pnl { get; init; }

    public decimal RealizedPnl { get; init; }

    public decimal UnrealizedPnl { get; init; }

    public decimal ReturnPercent { get; init; }

    public decimal MaxDrawdownPercent { get; init; }

    public decimal? ReturnToDrawdown { get; init; }

    public int ClosedTrades { get; init; }

    public decimal? WinRatePercent { get; init; }

    public int Fills { get; init; }

    public decimal Fees { get; init; }

    public decimal MaxPositionCost { get; init; }

    public decimal MaxPositionCostPercent { get; init; }

    public decimal MaxCapitalUsed { get; init; }

    public decimal TimeInPositionPercent { get; init; }

    public decimal TimeUnderWaterPercent { get; init; }

    public int LongestUnderWaterCandles { get; init; }

    public long SkippedOrders { get; init; }

    public int Candles { get; init; }

    public TimeSpan CalculationTime { get; init; }
}