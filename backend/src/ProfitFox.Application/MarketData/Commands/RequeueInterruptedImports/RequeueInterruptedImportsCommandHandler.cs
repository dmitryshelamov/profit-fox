using MediatR;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.RequeueInterruptedImports;

public sealed class RequeueInterruptedImportsCommandHandler(ICandleImportJobRepository jobs, ICandleImportQueue queue, TimeProvider time)
    : IRequestHandler<RequeueInterruptedImportsCommand>
{
    public const string InterruptedByRestart = "Interrupted by service restart.";

    // A job that keeps stopping the service before it stores anything would otherwise restart it forever.
    private const int MaxStalledRestarts = 3;

    /// <summary>Queues the jobs in the order they were created; a running job counts one more stalled restart.</summary>
    public async Task Handle(RequeueInterruptedImportsCommand request, CancellationToken cancellationToken)
    {
        foreach (CandleImportJob unfinished in await jobs.GetUnfinishedAsync(cancellationToken))
        {
            CandleImportJob job = unfinished;

            if (job.Status == CandleImportStatus.Running)
            {
                job = job with { Status = CandleImportStatus.Pending, StalledRestarts = job.StalledRestarts + 1 };

                if (job.StalledRestarts == MaxStalledRestarts)
                {
                    job = job with { Status = CandleImportStatus.Failed, Error = InterruptedByRestart, FinishedAt = time.GetUtcNow() };
                }

                await jobs.UpdateAsync(job, cancellationToken);
            }

            if (job.Status == CandleImportStatus.Pending)
            {
                await queue.EnqueueAsync(job.Id, cancellationToken);
            }
        }
    }
}