using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.RunNextCandleImport;

public sealed class RunNextCandleImportCommandHandler(
    IMarketHistoryClient marketHistory,
    ICandleImportJobRepository jobs,
    ICandleRepository candles,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time) : IRequestHandler<RunNextCandleImportCommand, bool>
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>
    ///     Loads the closed candles of <c>[From, To)</c> that are not stored yet, page by page; saved pages survive a failure. While the
    ///     exchange is unavailable, the job tries again after each pause; a stored candle starts the pauses and the lost leases over. The
    ///     job runs while this Worker holds its lease; once the lease is lost, the job stops without another write.
    /// </summary>
    public async Task<bool> Handle(RunNextCandleImportCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        CandleImportJob? claimed = await jobs.ClaimNextAsync(worker.Id, now, now + settings.LeaseDuration, cancellationToken);

        if (claimed is null)
        {
            return false;
        }

        try
        {
            await RunClaimedAsync(claimed, now, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CandleImportWorkException(claimed.Id, exception);
        }

        return true;
    }

    private async Task RunClaimedAsync(CandleImportJob claimed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        CandleImportJob job = claimed.TakenOver(now);

        if (!await jobs.UpdateLeasedAsync(job, worker.Id, cancellationToken) || job.Status == CandleImportStatus.Failed)
        {
            return;
        }

        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var done = new CancellationTokenSource();
        Task keeping = LeaseKeeper.KeepAsync((until, token) => jobs.RenewLeaseAsync(job.Id, worker.Id, until, token), settings, time, work, done.Token);

        try
        {
            await RunAsync(work);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The lease is lost: the Worker that took it over goes on with the job.
        }
        finally
        {
            await done.CancelAsync();
            await keeping;
        }

        async Task RunAsync(CancellationTokenSource running)
        {
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
                        await Task.Delay(RetryDelays[pauses++], time, running.Token);
                    }
                }

                job = job with { Status = CandleImportStatus.Succeeded, FinishedAt = time.GetUtcNow() };
            }
            catch (Exception exception) when (!running.IsCancellationRequested)
            {
                job = job with { Status = CandleImportStatus.Failed, Error = exception.Message, FinishedAt = time.GetUtcNow() };
            }

            await jobs.UpdateLeasedAsync(job, worker.Id, running.Token);

            async Task ImportMissingAsync()
            {
                IReadOnlyList<DateTimeOffset> stored = await candles.GetOpenTimesAsync(job.Instrument, job.Interval, job.From, job.To, running.Token);

                foreach ((DateTimeOffset from, DateTimeOffset to) in job.Interval.MissingRanges(stored, job.From, job.To))
                {
                    await foreach (IReadOnlyList<Candle> page in marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, from, to, running.Token))
                    {
                        var closed = page
                            .Where(c => c.OpenTime >= from && c.OpenTime < to && c.IsClosedAt(job.Interval, now))
                            .ToList();
                        await candles.UpsertAsync(job.Instrument, job.Interval, closed, running.Token);

                        if (closed.Count > 0)
                        {
                            pauses = 0;
                            job = job with { LostLeases = 0 };
                        }

                        // The page is stored: its count must be saved even if a shutdown cancels the job meanwhile.
                        job = job with { ImportedCandles = job.ImportedCandles + closed.Count };

                        if (!await jobs.UpdateLeasedAsync(job, worker.Id, CancellationToken.None))
                        {
                            await running.CancelAsync();
                            running.Token.ThrowIfCancellationRequested();
                        }
                    }
                }
            }
        }
    }
}