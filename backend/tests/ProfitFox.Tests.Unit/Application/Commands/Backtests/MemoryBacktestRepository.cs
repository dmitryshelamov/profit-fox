using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Tests.Unit.Application.Commands.Backtests;

/// <summary>
///     The backtests in memory for the tests of the handlers that read them; the queue itself is the job of the database
///     repository and is tested there.
/// </summary>
public sealed class MemoryBacktestRepository : IBacktestRepository
{
    private readonly List<Backtest> _backtests = [];

    public Task<Backtest> EnqueueAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        _backtests.Add(backtest);
        return Task.FromResult(backtest);
    }

    public Task<Backtest?> ClaimAsync(string worker, TimeSpan lease, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<Renewal?> RenewAsync(string id, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<CancelOutcome> CancelAsync(string id, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task FinishAsync(RunAssignment assignment, BacktestOutcome outcome, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task StopAsync(RunAssignment assignment, string status, string reason, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<Backtest>> ActiveAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Backtest>>([.. _backtests.Where(backtest => backtest.IsActive)]);
    }

    public Task<IReadOnlyList<Backtest>> FinishedAsync(int limit, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Backtest>>([.. _backtests.Where(backtest => !backtest.IsActive).Take(limit)]);
    }

    public Task<Backtest?> FindAsync(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_backtests.FirstOrDefault(backtest => backtest.Id == id));
    }

    public Task<IReadOnlyList<PairSummary>?> PairsAsync(string id, int set, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // The first that waits or goes in the order of putting, otherwise the last that ended.
    public Task<IReadOnlyDictionary<Guid, Backtest>> LastOfStrategiesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, Backtest> last = _backtests
            .GroupBy(backtest => backtest.StrategyId)
            .ToDictionary(group => group.Key, group => group.FirstOrDefault(backtest => backtest.IsActive) ?? group.Last());
        return Task.FromResult(last);
    }
}
