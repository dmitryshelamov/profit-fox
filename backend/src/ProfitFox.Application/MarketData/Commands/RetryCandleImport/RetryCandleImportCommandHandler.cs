using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.RetryCandleImport;

public sealed class RetryCandleImportCommandHandler(ICandleImportJobRepository jobs) : IRequestHandler<RetryCandleImportCommand>
{
    /// <summary>
    ///     Makes a failed job pending again under the same id, for a Worker to take; it loads only the candles still missing. The status
    ///     check and the save are not atomic: a job retried twice is pending once.
    /// </summary>
    public async Task Handle(RetryCandleImportCommand request, CancellationToken cancellationToken)
    {
        CandleImportJob job = await jobs.GetAsync(request.JobId, cancellationToken) ?? throw new NotFoundException("The import job does not exist.");

        if (job.Status != CandleImportStatus.Failed)
        {
            throw new ConflictException($"Only a failed import job can be retried; this one is {job.Status}.");
        }

        await jobs.UpdateAsync(
            job with { Status = CandleImportStatus.Pending, Error = null, FinishedAt = null, LostLeases = 0 },
            cancellationToken);
    }
}