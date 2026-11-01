using MediatR;

using ProfitFox.Application.Ports.Backtests;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Commands.RequeueInterruptedBacktests;

public sealed class RequeueInterruptedBacktestsCommandHandler(IBacktestRepository backtests, IBacktestQueue queue, TimeProvider time)
    : IRequestHandler<RequeueInterruptedBacktestsCommand>
{
    /// <summary>
    ///     Queues the backtests in the order they were created; a running one counts one more stalled restart and resumes with the
    ///     combinations that have no result yet, unless that was its third restart in a row without a new result.
    /// </summary>
    public async Task Handle(RequeueInterruptedBacktestsCommand request, CancellationToken cancellationToken)
    {
        foreach (Backtest unfinished in await backtests.GetUnfinishedAsync(cancellationToken))
        {
            Backtest backtest = unfinished;

            if (backtest.Status == BacktestStatus.Running)
            {
                backtest = backtest.Restarted(time.GetUtcNow());
                await backtests.UpdateAsync(backtest, BacktestStatus.Running, cancellationToken);
            }

            if (backtest.Status != BacktestStatus.Failed)
            {
                await queue.EnqueueAsync(backtest.Id, cancellationToken);
            }
        }
    }
}