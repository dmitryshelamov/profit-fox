using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.RunCandleImport;

public sealed class RunCandleImportCommandHandler(
    IMarketHistoryClient marketHistory,
    ICandleImportJobRepository jobs,
    ICandleRepository candles,
    TimeProvider time) : IRequestHandler<RunCandleImportCommand>
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>
    ///     Loads the closed candles of <c>[From, To)</c> that are not stored yet, page by page; saved pages survive a failure. While the
    ///     exchange is unavailable, the job tries again after each pause; a stored candle starts the pauses and the stalled restarts over.
    /// </summary>
    public async Task Handle(RunCandleImportCommand request, CancellationToken cancellationToken)
    {
        CandleImportJob job = (await jobs.GetAsync(request.JobId, cancellationToken))!;
        DateTimeOffset now = time.GetUtcNow();
        job = job with { Status = CandleImportStatus.Running, StartedAt = now };
        await jobs.UpdateAsync(job, cancellationToken);
        var pauses = 0;

        try
        {
            while (true)
            {
                try
                {
                    await ImportMissingAsync();

                    break;
                }
                catch (ExchangeUnavailableException) when (pauses < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[pauses++], time, cancellationToken);
                }
            }

            job = job with { Status = CandleImportStatus.Succeeded, FinishedAt = time.GetUtcNow() };
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            job = job with { Status = CandleImportStatus.Failed, Error = exception.Message, FinishedAt = time.GetUtcNow() };
        }

        await jobs.UpdateAsync(job, cancellationToken);

        async Task ImportMissingAsync()
        {
            IReadOnlyList<DateTimeOffset> stored = await candles.GetOpenTimesAsync(job.Instrument, job.Interval, job.From, job.To, cancellationToken);

            foreach ((DateTimeOffset from, DateTimeOffset to) in job.Interval.MissingRanges(stored, job.From, job.To))
            {
                await foreach (IReadOnlyList<Candle> page in marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, from, to, cancellationToken))
                {
                    var closed = page
                        .Where(c => c.OpenTime >= from && c.OpenTime < to && c.IsClosedAt(job.Interval, now))
                        .ToList();
                    await candles.UpsertAsync(job.Instrument, job.Interval, closed, cancellationToken);

                    if (closed.Count > 0)
                    {
                        pauses = 0;
                        job = job with { StalledRestarts = 0 };
                    }

                    // The page is stored: its count must be saved even if a shutdown cancels the job meanwhile.
                    job = job with { ImportedCandles = job.ImportedCandles + closed.Count };
                    await jobs.UpdateAsync(job, CancellationToken.None);
                }
            }
        }
    }
}