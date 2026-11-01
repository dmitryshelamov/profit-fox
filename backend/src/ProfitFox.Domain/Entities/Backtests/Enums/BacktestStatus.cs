namespace ProfitFox.Domain.Entities.Backtests.Enums;

public enum BacktestStatus
{
    Pending,
    LoadingCandles,
    Running,
    Succeeded,
    Failed,
    Cancelled
}