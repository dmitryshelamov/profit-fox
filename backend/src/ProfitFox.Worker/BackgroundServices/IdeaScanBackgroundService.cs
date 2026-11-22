using Npgsql;

using ProfitFox.Application.Commands.Ideas.RunScanner;
using ProfitFox.Application.Commands.Runs.RunNext;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Worker.Logging;

namespace ProfitFox.Worker.BackgroundServices;

/// <summary>
///     The lane of the Worker for ideas (change add-ideas-and-market, design.md, decision 1): one Worker of the bench holds the
///     lease of the scanner and keeps the live data; the others try to take it every <see cref="WorkerSettings.LeaseRenewal" />
///     and take the scanner over once the holder stopped. The lease is renewed and kept as that of a backtest. The runs of the
///     queue go on beside it in their own lane.
/// </summary>
internal sealed class IdeaScanBackgroundService(
    NpgsqlDataSource database,
    IServiceScopeFactory scopes,
    IScannerLeaseRepository lease,
    WorkerSettings worker,
    ScannerSettings settings,
    TimeProvider time,
    ILogger<IdeaScanBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            await BenchSchema.WaitFor(database, worker.SchemaPollInterval, new LogWriter(logger), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                bool taken = false;
                try
                {
                    taken = await lease.TakeAsync(worker.Name, worker.LeaseDuration, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "The Worker could not ask for the lease of the scanner.");
                }

                if (taken)
                {
                    await Scan(stoppingToken);
                }
                else
                {
                    await Task.Delay(worker.LeaseRenewal, time, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker is shut down.
        }
        finally
        {
            try
            {
                await lease.ReleaseAsync(worker.Name, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The Worker could not give the lease of the scanner up.");
            }
        }
    }

    // The scanner while the lease holds.
    private async Task Scan(CancellationToken stoppingToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var done = new CancellationTokenSource();
        Task keeper = LeaseKeeper.KeepAsync(token => lease.TakeAsync(worker.Name, worker.LeaseDuration, token), worker, time, work, done.Token);
        try
        {
            logger.LogInformation("Worker {Name} keeps the live data.", worker.Name);
            await scopes.SendAsync(new RunScannerCommand(worker.Name), work.Token);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            if (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The scanner of ideas stopped.");
            }
        }
        finally
        {
            await done.CancelAsync();
            await keeper;
        }
    }
}
