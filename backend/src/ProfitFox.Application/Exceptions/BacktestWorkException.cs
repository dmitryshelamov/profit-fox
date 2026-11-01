namespace ProfitFox.Application.Exceptions;

/// <summary>Work on a backtest failed outside its own error handling; the Worker logs it with the backtest and goes on.</summary>
public sealed class BacktestWorkException(Guid backtestId, Exception inner)
    : Exception($"Work on backtest {backtestId} failed: {inner.Message}", inner)
{
    public Guid BacktestId { get; } = backtestId;
}