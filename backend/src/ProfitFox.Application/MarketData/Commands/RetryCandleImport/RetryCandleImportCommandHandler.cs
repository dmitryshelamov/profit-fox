using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.RetryCandleImport;

public sealed class RetryCandleImportCommandHandler(ICandleImportJobRepository jobs, ICandleImportQueue queue) : IRequestHandler<RetryCandleImportCommand>
{
    /// <summary>
    ///     Queues a failed job again under the same id; it loads only the candles still missing. The status check and the save are
    ///     not atomic: a job queued twice runs twice, and the second run finds nothing left to store.
    /// </summary>
    public async Task Handle(RetryCandleImportCommand request, CancellationToken cancellationToken)
    {
        CandleImportJob job = await jobs.GetAsync(request.JobId, cancellationToken) ?? throw new NotFoundException("The import job does not exist.");

        if (job.Status != CandleImportStatus.Failed)
        {
            throw new ConflictException($"Only a failed import job can be retried; this one is {job.Status}.");
        }

        await jobs.UpdateAsync(
            job with { Status = CandleImportStatus.Pending, Error = null, FinishedAt = null, InterruptionsWithoutProgress = 0 },
            cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
    }
}