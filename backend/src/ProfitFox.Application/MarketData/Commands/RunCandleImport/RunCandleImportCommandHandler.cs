using MediatR;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.RunCandleImport;

public sealed class RunCandleImportCommandHandler(
    IMarketHistoryClient marketHistory,
    ICandleImportJobRepository jobs,
    ICandleRepository candles,
    TimeProvider time) : IRequestHandler<RunCandleImportCommand>
{
    /// <summary>Loads the job's period page by page, storing only closed candles of <c>[From, To)</c>; saved pages survive a failure.</summary>
    public async Task Handle(RunCandleImportCommand request, CancellationToken cancellationToken)
    {
        var job = (await jobs.GetAsync(request.JobId, cancellationToken))!;
        var now = time.GetUtcNow();
        job = job with { Status = CandleImportStatus.Running, StartedAt = now };
        await jobs.UpdateAsync(job, cancellationToken);
        try
        {
            await foreach (var page in marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, job.From, job.To, cancellationToken))
            {
                var closed = page
                    .Where(c => c.OpenTime >= job.From && c.OpenTime < job.To && c.IsClosedAt(job.Interval, now))
                    .ToList();
                await candles.UpsertAsync(job.Instrument, job.Interval, closed, cancellationToken);
                job = job with { ImportedCandles = job.ImportedCandles + closed.Count };
                await jobs.UpdateAsync(job, cancellationToken);
            }

            job = job with { Status = CandleImportStatus.Succeeded, FinishedAt = time.GetUtcNow() };
        }
        catch (Exception exception)
        {
            job = job with { Status = CandleImportStatus.Failed, Error = exception.Message, FinishedAt = time.GetUtcNow() };
        }

        await jobs.UpdateAsync(job, cancellationToken);
    }
}
