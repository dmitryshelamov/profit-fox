namespace ProfitFox.Application.Ports.Backtests;

/// <summary>Signals the background worker that a backtest is waiting; its state lives in the store.</summary>
public interface IBacktestQueue
{
    ValueTask EnqueueAsync(Guid backtestId, CancellationToken cancellationToken);
}